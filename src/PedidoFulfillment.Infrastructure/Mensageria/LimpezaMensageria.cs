using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Limpeza periódica dos dados de mensageria e de idempotência (REQ-14, R-01): eventos
/// enviados, registros de processado, chaves de idempotência e pedidos de reenvio finais, cada
/// um depois da sua retenção. Uma instância por rodada, pelo lock de aplicação; cada lote é um
/// <c>DELETE TOP</c> curto, para não disputar com o despachante, os consumidores e a API.
/// </summary>
internal sealed class LimpezaMensageria(
    IServiceScopeFactory escopos,
    IOptions<OpcoesLimpeza> opcoes,
    IOptions<OpcoesSqlServer> sqlServer,
    TimeProvider relogio,
    ILogger<LimpezaMensageria> logger) : BackgroundService
{
    /// <summary>Nome do lock de aplicação; outra instância do Worker que não o consegue pula a rodada.</summary>
    internal const string RecursoLock = "oms-limpeza";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuracao = opcoes.Value;
        if (!configuracao.Habilitada)
        {
            return;
        }

        using var temporizador = new PeriodicTimer(configuracao.Intervalo, relogio);
        try
        {
            do
            {
                await ExecutarRodadaAsync(configuracao, stoppingToken);
            }
            while (await temporizador.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento do Worker.
        }
    }

    internal async Task ExecutarRodadaAsync(OpcoesLimpeza configuracao, CancellationToken cancellationToken)
    {
        try
        {
            await using var conexao = new SqlConnection(sqlServer.Value.StringConexao);
            await conexao.OpenAsync(cancellationToken);
            if (!await LockAplicacao.AdquirirAsync(conexao, RecursoLock, cancellationToken))
            {
                EventosLimpezaReenvio.RodadaIgnorada(logger, "limpeza");
                return;
            }

            try
            {
                await LimparAsync(configuracao, cancellationToken);
            }
            finally
            {
                await LockAplicacao.LiberarAsync(conexao, RecursoLock);
            }
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Banco fora ou erro inesperado: tenta na próxima rodada, sem derrubar o processo.
            EventosLimpezaReenvio.FalhaLimpeza(logger, excecao.GetType().Name, excecao.Message);
        }
    }

    private async Task LimparAsync(OpcoesLimpeza configuracao, CancellationToken cancellationToken)
    {
        var agora = relogio.GetUtcNow();
        var limiteEnviados = agora - configuracao.RetencaoEnviados;
        var limiteProcessados = agora - configuracao.RetencaoProcessados;
        var limiteChaves = agora - configuracao.RetencaoChaves;
        var limitePedidos = agora - configuracao.RetencaoPedidosReenvio;

        var enviados = await EmLotesAsync(configuracao, (contexto, lote, ct) => contexto.EventosSaida
            .Where(e => e.EnviadoEm != null && e.EnviadoEm < limiteEnviados)
            .OrderBy(e => e.EnviadoEm).Take(lote).ExecuteDeleteAsync(ct), cancellationToken);
        var processados = await EmLotesAsync(configuracao, (contexto, lote, ct) => contexto.EventosProcessados
            .Where(p => p.ProcessadoEm < limiteProcessados)
            .OrderBy(p => p.ProcessadoEm).Take(lote).ExecuteDeleteAsync(ct), cancellationToken);
        var chaves = await EmLotesAsync(configuracao, (contexto, lote, ct) => contexto.ChavesIdempotencia
            .Where(c => c.CriadoEm < limiteChaves)
            .OrderBy(c => c.CriadoEm).Take(lote).ExecuteDeleteAsync(ct), cancellationToken);
        var pedidos = await EmLotesAsync(configuracao, (contexto, lote, ct) => contexto.PedidosReenvioDlq
            .Where(p => (p.Situacao == SituacaoReenvioDlq.Concluido || p.Situacao == SituacaoReenvioDlq.Falhou) && p.RegistradoEm < limitePedidos)
            .OrderBy(p => p.RegistradoEm).Take(lote).ExecuteDeleteAsync(ct), cancellationToken);

        var nivel = enviados + processados + chaves + pedidos > 0 ? LogLevel.Information : LogLevel.Debug;
        EventosLimpezaReenvio.LimpezaConcluida(logger, nivel, enviados, processados, chaves, pedidos);
    }

    /// <summary>Até <c>LotesPorRodada</c> lotes, cada um num escopo e numa transação curta; para quando um lote vem incompleto.</summary>
    private async Task<int> EmLotesAsync(
        OpcoesLimpeza configuracao, Func<ContextoBancoDados, int, CancellationToken, Task<int>> apagar, CancellationToken cancellationToken)
    {
        var total = 0;
        for (var lote = 0; lote < configuracao.LotesPorRodada; lote++)
        {
            await using var escopo = escopos.CreateAsyncScope();
            var apagados = await apagar(escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>(), configuracao.TamanhoLote, cancellationToken);
            total += apagados;
            if (apagados < configuracao.TamanhoLote)
            {
                break;
            }
        }

        return total;
    }
}
