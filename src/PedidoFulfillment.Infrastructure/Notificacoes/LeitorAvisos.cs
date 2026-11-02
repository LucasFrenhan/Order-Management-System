using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Notificacoes;

/// <summary>
/// Leitura periódica dos avisos na API, uma por instância e não por conexão (REQ-18, FR-024,
/// research R-16): começa no que já está confirmado (não relê o passado) e publica o que é novo no
/// <see cref="DistribuidorAvisos"/>. Sem broker: a API continua sem permissão nele (REQ-14). Uma
/// falha vira log e nova tentativa no ciclo seguinte; as conexões continuam com o sinal de vida.
/// </summary>
internal sealed class LeitorAvisos(
    IServiceScopeFactory escopos,
    DistribuidorAvisos distribuidor,
    IOptions<OpcoesNotificacoes> opcoes,
    TimeProvider relogio,
    ILogger<LeitorAvisos> logger) : BackgroundService
{
    private const int Lote = 1000;

    private ulong? cursor;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(opcoes.Value.Sse.IntervaloLeitura, relogio);
        try
        {
            do
            {
                await LerAsync(stoppingToken);
            }
            while (await temporizador.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento da API.
        }
    }

    internal async Task LerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            var leitura = escopo.ServiceProvider.GetRequiredService<LeituraAvisos>();
            cursor ??= await leitura.PosicaoAtualAsync(cancellationToken);
            while (true)
            {
                var avisos = await leitura.LerAsync(cursor.Value, Lote, cancellationToken);
                if (avisos.Count == 0)
                {
                    return;
                }

                distribuidor.Publicar(avisos);
                cursor = avisos[^1].Posicao;
                if (avisos.Count < Lote)
                {
                    return;
                }
            }
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            EventosNotificacao.FalhaLeitura(logger, "sse", $"{excecao.GetType().Name}: {excecao.Message}");
        }
    }
}
