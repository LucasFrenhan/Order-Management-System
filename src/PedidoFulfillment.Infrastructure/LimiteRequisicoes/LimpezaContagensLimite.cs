using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Infrastructure.Observabilidade;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.LimiteRequisicoes;

/// <summary>
/// Remove as contagens vencidas do limite de requisições (REQ-23, FR-011, R-07), no Worker. Uma rotina
/// própria, e não um alvo da <see cref="LimpezaMensageria"/>: ela roda de hora em hora, e poucas origens
/// já criariam contagens mais depressa. Uma instância por rodada, pelo relógio do banco (FR-008) e com
/// prioridade baixa em um deadlock, para quem perde ser a limpeza, e não a requisição (R-03).
/// </summary>
internal sealed class LimpezaContagensLimite(
    IServiceScopeFactory escopos,
    IOptions<OpcoesLimpeza> opcoes,
    IOptions<OpcoesSqlServer> sqlServer,
    TimeProvider relogio,
    ILogger<LimpezaContagensLimite> logger) : BackgroundService
{
    internal const string RecursoLock = "oms-limpeza-contagens";

    /// <summary>Valor de <c>oms.limpeza.alvo</c>.</summary>
    internal const string AlvoContagens = "contagens_limite";

    private const string NomeRotina = "limpeza de contagens";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuracao = opcoes.Value;
        if (!configuracao.Habilitada)
        {
            return;
        }

        using var temporizador = new PeriodicTimer(configuracao.IntervaloContagens, relogio);
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
                EventosLimpezaReenvio.RodadaIgnorada(logger, NomeRotina);
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
            // Banco fora ou erro inesperado: tenta na próxima rodada. A limitação continua certa,
            // pois a janela vencida conta como zero; só a tabela cresce.
            EventosLimpezaReenvio.FalhaLimpeza(logger, excecao.GetType().Name, excecao.Message);
        }
    }

    private async Task LimparAsync(OpcoesLimpeza configuracao, CancellationToken cancellationToken)
    {
        var total = 0;
        for (var lote = 0; lote < configuracao.LotesContagensPorRodada; lote++)
        {
            await using var escopo = escopos.CreateAsyncScope();
            var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
            var apagados = await contexto.Database.ExecuteSqlAsync(
                $"SET DEADLOCK_PRIORITY LOW; DELETE TOP ({configuracao.TamanhoLote}) FROM ContagensLimite WHERE FimJanela < SYSUTCDATETIME();",
                cancellationToken);
            total += apagados;
            if (apagados < configuracao.TamanhoLote)
            {
                break;
            }
        }

        TelemetriaOms.LinhasApagadas.Add(total, new KeyValuePair<string, object?>(TelemetriaOms.Atributos.LimpezaAlvo, AlvoContagens));
    }
}
