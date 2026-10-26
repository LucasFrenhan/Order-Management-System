using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Observabilidade;

namespace PedidoFulfillment.Infrastructure.Pagamentos;

/// <summary>
/// Rodada dos estornos pendentes no Worker (REQ-13, R-04), no molde do
/// <c>ReconciliadorPedidos</c>: a primeira na partida, depois uma a cada intervalo. Uma instância
/// por rodada, pelo lock de aplicação do SQL Server, para o gateway não ser chamado em paralelo
/// para o mesmo pedido; cada pedido num escopo próprio, decidido pelo <see cref="ServicoEstornos"/>.
/// </summary>
internal sealed class ProcessadorEstornos(
    IServiceScopeFactory escopos,
    IOptions<OpcoesEstornos> opcoes,
    IOptions<OpcoesSqlServer> sqlServer,
    TimeProvider relogio,
    ILogger<ProcessadorEstornos> logger) : BackgroundService
{
    /// <summary>Nome do lock de aplicação; outra instância do Worker que não o consegue pula a rodada.</summary>
    internal const string RecursoLock = "oms-estornos";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuracao = opcoes.Value;
        if (!configuracao.Habilitado)
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

    internal async Task ExecutarRodadaAsync(OpcoesEstornos configuracao, CancellationToken cancellationToken)
    {
        try
        {
            // Conexão própria, aberta até o fim da rodada: o lock é da sessão (REQ-12, R-08).
            await using var conexao = new SqlConnection(sqlServer.Value.StringConexao);
            await conexao.OpenAsync(cancellationToken);
            if (!await AdquirirLockAsync(conexao, cancellationToken))
            {
                EventosPagamento.RodadaEstornosIgnorada(logger);
                return;
            }

            try
            {
                // Um trace por rodada efetiva; a rodada sem o lock não gera span (REQ-16, R-05).
                using var rodada = TelemetriaOms.Fonte.StartActivity(TelemetriaOms.Spans.ProcessarEstornos, ActivityKind.Internal, default(ActivityContext));
                rodada?.SetTag(TelemetriaOms.Atributos.Rotina, TelemetriaOms.Rotinas.Estornos);
                await EstornarLoteAsync(configuracao, cancellationToken);
            }
            finally
            {
                // A conexão volta ao pool sem encerrar a sessão: o lock é liberado aqui.
                await LiberarLockAsync(conexao);
            }
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Banco fora ou erro inesperado: tenta na próxima rodada, sem derrubar o processo.
            EventosPagamento.FalhaRodadaEstornos(logger, excecao.GetType().Name, excecao.Message);
        }
    }

    private async Task EstornarLoteAsync(OpcoesEstornos configuracao, CancellationToken cancellationToken)
    {
        IReadOnlyList<EstornoPendente> lote;
        await using (var escopo = escopos.CreateAsyncScope())
        {
            lote = await escopo.ServiceProvider.GetRequiredService<IConsultaEstornosPendentes>()
                .ListarAsync(configuracao.TamanhoLote, cancellationToken);
        }

        var parametros = new ParametrosAlertaEstorno(configuracao.LimiteAlerta, configuracao.TentativasAlerta);
        int confirmados = 0, falhas = 0;
        foreach (var pendente in lote)
        {
            switch (await EstornarAsync(pendente, parametros, cancellationToken))
            {
                case ResultadoRodadaEstorno.Confirmado:
                    confirmados++;
                    break;
                case ResultadoRodadaEstorno.Falhou:
                    falhas++;
                    break;
            }
        }

        var nivel = confirmados > 0 || falhas > 0 ? LogLevel.Information : LogLevel.Debug;
        EventosPagamento.ResumoEstornos(logger, nivel, confirmados, falhas);
    }

    /// <summary>Um escopo por pedido: a falha de um não interrompe os demais.</summary>
    private async Task<ResultadoRodadaEstorno> EstornarAsync(EstornoPendente pendente, ParametrosAlertaEstorno parametros, CancellationToken cancellationToken)
    {
        var pedidoId = pendente.PedidoId;
        // Passo da rodada, ligado ao trace de criação do pedido (REQ-16, R-05).
        using var span = TelemetriaOms.Fonte.StartActivity(
            TelemetriaOms.Spans.EstornarPedido, ActivityKind.Internal, default(ActivityContext),
            links: TelemetriaOms.LigacaoPara(pendente.ContextoRastreio));
        span?.SetTag(TelemetriaOms.Atributos.PedidoId, pedidoId.ToString());
        ResultadoRodadaEstorno resultado;
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            resultado = await escopo.ServiceProvider.GetRequiredService<ServicoEstornos>().EstornarAsync(pedidoId, parametros, cancellationToken);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Banco fora no meio ou erro inesperado: a próxima rodada trata o pedido de novo.
            EventosPagamento.FalhaRodadaEstornos(logger, excecao.GetType().Name, $"pedido {pedidoId}: {excecao.Message}");
            span.MarcarErro(excecao.GetType().Name);
            resultado = ResultadoRodadaEstorno.Falhou;
        }

        span?.SetTag(TelemetriaOms.Atributos.Resultado, resultado switch
        {
            ResultadoRodadaEstorno.Confirmado => TelemetriaOms.Resultados.Confirmado,
            ResultadoRodadaEstorno.Falhou => TelemetriaOms.Resultados.Falhou,
            _ => null
        });
        return resultado;
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
