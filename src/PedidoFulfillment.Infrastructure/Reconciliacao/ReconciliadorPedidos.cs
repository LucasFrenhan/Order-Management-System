using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Reconciliacao;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Reconciliacao;

/// <summary>
/// Ciclo da reconciliação de pedidos parados no Worker (REQ-12, R-08 e R-09): o primeiro na
/// partida, depois um a cada intervalo. Uma instância por ciclo, pelo lock de aplicação do SQL
/// Server; cada pedido num escopo próprio, decidido pelo <see cref="ServicoReconciliacao"/>.
/// </summary>
internal sealed class ReconciliadorPedidos(
    IServiceScopeFactory escopos,
    IOptions<OpcoesReconciliacao> opcoes,
    IOptions<OpcoesSqlServer> sqlServer,
    RegistroFalhasReconciliacao registro,
    TimeProvider relogio,
    ILogger<ReconciliadorPedidos> logger) : BackgroundService
{
    /// <summary>Nome do lock de aplicação; outra instância do Worker que não o consegue pula o ciclo.</summary>
    internal const string RecursoLock = "oms-reconciliacao";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuracao = opcoes.Value;
        if (!configuracao.Habilitada)
        {
            return;
        }

        using var temporizador = new PeriodicTimer(configuracao.IntervaloCiclo, relogio);
        try
        {
            do
            {
                await ExecutarCicloAsync(configuracao, stoppingToken);
            }
            while (await temporizador.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento do Worker.
        }
    }

    internal async Task ExecutarCicloAsync(OpcoesReconciliacao configuracao, CancellationToken cancellationToken)
    {
        try
        {
            // Conexão própria, aberta até o fim do ciclo: o lock é da sessão (R-08).
            await using var conexao = new SqlConnection(sqlServer.Value.StringConexao);
            await conexao.OpenAsync(cancellationToken);
            if (!await AdquirirLockAsync(conexao, cancellationToken))
            {
                EventosReconciliacao.CicloIgnorado(logger);
                return;
            }

            try
            {
                await ReconciliarLoteAsync(configuracao, cancellationToken);
            }
            finally
            {
                // A conexão volta ao pool sem encerrar a sessão: o lock é liberado aqui.
                await LiberarLockAsync(conexao);
            }
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Banco fora ou erro inesperado: tenta no próximo ciclo, sem derrubar o processo.
            EventosReconciliacao.FalhaCiclo(logger, excecao.GetType().Name, excecao.Message);
        }
    }

    private async Task ReconciliarLoteAsync(OpcoesReconciliacao configuracao, CancellationToken cancellationToken)
    {
        var agora = relogio.GetUtcNow();
        registro.Podar(agora - configuracao.LimiteEsgotamento);
        IReadOnlyList<PedidoParado> lote;
        await using (var escopo = escopos.CreateAsyncScope())
        {
            lote = await escopo.ServiceProvider.GetRequiredService<IConsultaPedidosParados>()
                .ListarAsync(agora - configuracao.LimiteParado, configuracao.TamanhoLote, registro.ComFalha, cancellationToken);
        }

        var parametros = new ParametrosEsgotamento(configuracao.LimiteEsgotamento, configuracao.CiclosFalhaEsgotamento);
        var recuperados = new Dictionary<EtapaFluxoPedido, int>();
        int esgotados = 0, falhas = 0;
        foreach (var parado in lote)
        {
            switch (await ReconciliarAsync(parado, parametros, cancellationToken))
            {
                case ResultadoReconciliacao.Recuperado:
                    recuperados[parado.Etapa] = recuperados.GetValueOrDefault(parado.Etapa) + 1;
                    break;
                case ResultadoReconciliacao.Esgotado:
                    esgotados++;
                    break;
                case ResultadoReconciliacao.Falhou:
                    falhas++;
                    break;
            }
        }

        var nivel = recuperados.Count > 0 || esgotados > 0 ? LogLevel.Warning : LogLevel.Debug;
        EventosReconciliacao.Resumo(logger, nivel,
            recuperados.GetValueOrDefault(EtapaFluxoPedido.Estoque),
            recuperados.GetValueOrDefault(EtapaFluxoPedido.Pagamento),
            recuperados.GetValueOrDefault(EtapaFluxoPedido.Entrega),
            esgotados, falhas);
    }

    /// <summary>Um escopo por pedido: a falha de um não interrompe os demais (FR-011).</summary>
    private async Task<ResultadoReconciliacao> ReconciliarAsync(PedidoParado parado, ParametrosEsgotamento parametros, CancellationToken cancellationToken)
    {
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            return await escopo.ServiceProvider.GetRequiredService<ServicoReconciliacao>()
                .ReconciliarAsync(parado, relogio.GetUtcNow(), parametros, cancellationToken);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var contagem = registro.RegistrarFalha(parado.PedidoId, relogio.GetUtcNow());
            EventosReconciliacao.FalhaPedido(logger, parado.PedidoId, parado.Etapa, contagem, $"{excecao.GetType().Name}: {excecao.Message}");
            return ResultadoReconciliacao.Falhou;
        }
    }

    private static async Task<bool> AdquirirLockAsync(SqlConnection conexao, CancellationToken cancellationToken)
    {
        await using var comando = new SqlCommand(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;",
            conexao);
        comando.Parameters.AddWithValue("@recurso", RecursoLock);
        return (int)(await comando.ExecuteScalarAsync(cancellationToken))! >= 0;
    }

    private static async Task LiberarLockAsync(SqlConnection conexao)
    {
        try
        {
            await using var comando = new SqlCommand("EXEC sp_releaseapplock @Resource = @recurso, @LockOwner = 'Session';", conexao);
            comando.Parameters.AddWithValue("@recurso", RecursoLock);
            await comando.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (SqlException)
        {
            // Sessão já encerrada: o lock foi junto.
        }
    }
}
