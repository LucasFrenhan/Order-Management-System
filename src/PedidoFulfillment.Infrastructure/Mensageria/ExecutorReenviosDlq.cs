using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Observabilidade;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Executa os pedidos de reenvio da DLQ registrados pela API (REQ-14, R-03): uma instância por
/// rodada, pelo lock de aplicação; primeiro os em execução (de uma execução que caiu), depois
/// os pendentes, um de cada vez, na ordem de registro. O canal é aberto antes de marcar o
/// pedido: com o broker fora, os pedidos continuam pendentes (analyze I2).
/// </summary>
internal sealed class ExecutorReenviosDlq(
    IServiceScopeFactory escopos,
    ReenvioDlq reenvio,
    IOptions<OpcoesReenvioDlq> opcoes,
    IOptions<OpcoesSqlServer> sqlServer,
    TimeProvider relogio,
    ILogger<ExecutorReenviosDlq> logger) : BackgroundService
{
    internal const string RecursoLock = "oms-reenvio-dlq";

    /// <summary>Pedidos tratados por rodada; os demais ficam para a seguinte.</summary>
    private const int PedidosPorRodada = 20;

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
                await ExecutarRodadaAsync(stoppingToken);
            }
            while (await temporizador.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento do Worker.
        }
    }

    internal async Task ExecutarRodadaAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var conexao = new SqlConnection(sqlServer.Value.StringConexao);
            await conexao.OpenAsync(cancellationToken);
            if (!await LockAplicacao.AdquirirAsync(conexao, RecursoLock, cancellationToken))
            {
                EventosLimpezaReenvio.RodadaIgnorada(logger, "reenvio da DLQ");
                return;
            }

            try
            {
                await ExecutarPedidosAsync(cancellationToken);
            }
            finally
            {
                await LockAplicacao.LiberarAsync(conexao, RecursoLock);
            }
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            EventosLimpezaReenvio.ReenvioFalhou(logger, Guid.Empty, "-", 0, "rodada", $"{excecao.GetType().Name}: {excecao.Message}");
        }
    }

    private async Task ExecutarPedidosAsync(CancellationToken cancellationToken)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var armazenamento = escopo.ServiceProvider.GetRequiredService<IArmazenamentoReenvios>();
        var pedidos = await armazenamento.ListarParaExecucaoAsync(PedidosPorRodada, cancellationToken);
        if (pedidos.Count == 0)
        {
            return;
        }

        RabbitMQ.Client.IChannel canal;
        try
        {
            canal = await reenvio.AbrirCanalAsync(cancellationToken);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Broker fora: nada é marcado, e os pedidos são tentados na rodada seguinte.
            foreach (var pedido in pedidos)
            {
                EventosLimpezaReenvio.ReenvioFalhou(logger, pedido.Id, pedido.Consumidor, 0, pedido.Situacao.ToString(), $"{excecao.GetType().Name}: {excecao.Message}");
            }

            return;
        }

        await using (canal)
        {
            var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
            foreach (var pedido in pedidos)
            {
                // Um trace por execução, ligado à requisição do Admin que registrou o pedido (REQ-16, R-04).
                var contextoRegistro = contexto.Entry(pedido).Property<string?>(ContextoBancoDados.ContextoRastreio).CurrentValue;
                using var span = TelemetriaOms.Fonte.StartActivity(
                    TelemetriaOms.Spans.ReenviarDlq(pedido.Consumidor), ActivityKind.Internal, default(ActivityContext),
                    links: TelemetriaOms.LigacaoPara(contextoRegistro));
                span?.SetTag(TelemetriaOms.Atributos.ReenvioPedidoId, pedido.Id.ToString());
                span?.SetTag(TelemetriaOms.Atributos.Consumidor, pedido.Consumidor);
                pedido.Iniciar(relogio.GetUtcNow());
                await armazenamento.SalvarAsync(cancellationToken);
                try
                {
                    var contagens = await reenvio.ExecutarAsync(canal, pedido, cancellationToken);
                    pedido.Concluir(contagens, relogio.GetUtcNow());
                    await armazenamento.SalvarAsync(cancellationToken);
                    EventosLimpezaReenvio.ReenvioConcluido(logger, pedido.Id, pedido.AutorId, pedido.Consumidor, pedido.Filtro.ToString(),
                        contagens.Reenviadas, contagens.BloqueadasPeloLimite, contagens.ForaDoFiltro);
                    MarcarContagens(span, contagens, TelemetriaOms.Resultados.Concluido);
                }
                catch (ReenvioInterrompidoException interrupcao)
                {
                    pedido.Falhar(interrupcao.Parciais, interrupcao.Message, relogio.GetUtcNow());
                    await armazenamento.SalvarAsync(cancellationToken);
                    EventosLimpezaReenvio.ReenvioFalhou(logger, pedido.Id, pedido.Consumidor, interrupcao.Parciais.Reenviadas, pedido.Situacao.ToString(), interrupcao.Message);
                    MarcarContagens(span, interrupcao.Parciais, TelemetriaOms.Resultados.Falhou);
                    span.MarcarErro(interrupcao.InnerException?.GetType().Name ?? nameof(ReenvioInterrompidoException));
                    // O canal caiu: os demais pedidos ficam para a rodada seguinte.
                    return;
                }
            }
        }
    }

    private static void MarcarContagens(Activity? span, ContagensReenvio contagens, string resultado)
    {
        span?.SetTag(TelemetriaOms.Atributos.ReenvioReenviadas, contagens.Reenviadas);
        span?.SetTag(TelemetriaOms.Atributos.ReenvioBloqueadas, contagens.BloqueadasPeloLimite);
        span?.SetTag(TelemetriaOms.Atributos.ReenvioForaDoFiltro, contagens.ForaDoFiltro);
        span?.SetTag(TelemetriaOms.Atributos.Resultado, resultado);
    }
}
