using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Observabilidade;

/// <summary>
/// Lê do banco os pendentes do outbox e a idade do mais antigo para as métricas 1 e 2 (REQ-16,
/// R-07), a cada <c>Observabilidade:IntervaloAmostragemOutboxSegundos</c>, só no Worker e só com
/// a telemetria ligada. Os gauges leem a última leitura, sem ir ao banco (FR-017). A consulta
/// usa o índice filtrado dos pendentes; sem lock, porque ler é idempotente.
/// </summary>
internal sealed partial class AmostradorOutbox(
    IServiceScopeFactory escopos,
    IConfiguration configuracao,
    IOptions<OpcoesObservabilidade> opcoes,
    TimeProvider relogio,
    ILogger<AmostradorOutbox> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OpcoesObservabilidade.Ligada(configuracao))
        {
            return;
        }

        using var temporizador = new PeriodicTimer(opcoes.Value.IntervaloAmostragemOutbox, relogio);
        try
        {
            do
            {
                await AmostrarAsync(stoppingToken);
            }
            while (await temporizador.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento do Worker.
        }
    }

    internal async Task AmostrarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            var pendentes = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>().EventosSaida.AsNoTracking()
                .Where(e => e.EnviadoEm == null);
            var quantidade = await pendentes.LongCountAsync(cancellationToken);
            var maisAntigo = await pendentes.MinAsync(e => (DateTimeOffset?)e.CriadoEm, cancellationToken);
            var idade = maisAntigo is { } criado ? Math.Max((relogio.GetUtcNow() - criado).TotalSeconds, 0) : 0;
            EstadoMetricas.RegistrarOutbox(quantidade, idade);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Banco fora: os gauges mantêm a última leitura; o despachante já avisa (5007).
            FalhaAmostragem(logger, excecao.GetType().Name, excecao.Message);
        }
    }

    // Debug sem ID novo de aviso (tasks T029): a falha do banco já aparece nos logs existentes.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Falha ao ler o outbox para as métricas ({Erro}): {Mensagem}")]
    private static partial void FalhaAmostragem(ILogger logger, string erro, string mensagem);
}
