using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Entregas;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Entregas.CenarioEntrega;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Entregas;

/// <summary>
/// Pedido que mudou antes do fulfillment, com SQL Server e RabbitMQ reais (REQ-10, US3;
/// research R-04 e R-06).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class PedidoIgnoradoNaEntregaTestes(MensageriaTeste mensageria)
{
    /// <summary>1 execução mais as 3 novas tentativas da base (REQ-06).</summary>
    private const int ExecucoesDaBase = 4;

    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(10);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    /// <summary>
    /// Pedido pago pelo Worker sem o consumidor de entrega, que é parado em seguida: o pedido fica
    /// em Processando, sem ordem, para a operação mexer nele.
    /// </summary>
    private async Task<Guid> PedidoPagoSemEntregaAsync(HttpClient cliente, string stringConexao)
    {
        await using var pagamento = await Pagamentos.CenarioPagamento.WorkerProntoAsync(mensageria, stringConexao, taxaAprovacao: 100);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2))).GetProperty("id").GetGuid();
        (await Pagamentos.CenarioPagamento.EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
        return pedidoId;
    }

    [Theory]
    [InlineData("Cancelado")]
    [InlineData("Despachado")]
    public async Task Pedido_cancelado_ou_despachado_antes_do_fulfillment_nao_ganha_ordem(string status)
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var fila = await mensageria.FilaTesteAsync(TiposEvento.EntregaCriada);
        using var logs = new CapturaLogs();
        var pedidoId = await PedidoPagoSemEntregaAsync(cliente, stringConexao);
        string[] passos = status == "Cancelado" ? ["Cancelado"] : ["ProntoParaEnvio", "Despachado"];
        foreach (var passo in passos)
        {
            (await MudarStatusAsync(operador, pedidoId, passo, "Operação")).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, stringConexao, logs);
        await PublicarPagamentoAprovadoAsync(mensageria, pedidoId);

        (await EsperarLogAsync(logs, 8002, Prazo)).Should().BeTrue("o pedido é ignorado e a mensagem confirmada");
        (await OrdensAsync(stringConexao, pedidoId)).Should().Be(0, "SC-003");
        (await EntregasCriadasAsync(stringConexao, pedidoId)).Should().Be(0);
        (await StatusAsync(stringConexao, pedidoId)).Should().Be(status);
        DoPedido(fila, pedidoId).Should().BeEmpty();
        (await EsperarNaDlqAsync(mensageria, pedidoId.ToString(), TimeSpan.FromMilliseconds(500), FilaEntrega)).Should().BeNull();
    }

    [Fact]
    public async Task Pedido_que_ja_tem_ordem_nao_ganha_outra()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        using var logs = new CapturaLogs();
        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, stringConexao, logs);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await EsperarOrdemAsync(stringConexao, pedidoId, Prazo)).Should().BeTrue();

        await PublicarPagamentoAprovadoAsync(mensageria, pedidoId);

        (await EsperarLogAsync(logs, 8002, Prazo)).Should().BeTrue("já tem ordem");
        (await OrdensAsync(stringConexao, pedidoId)).Should().Be(1);
        (await EntregasCriadasAsync(stringConexao, pedidoId)).Should().Be(1);
    }

    [Fact]
    public async Task Pedido_movido_para_pronto_para_envio_ganha_a_ordem_sem_nova_entrada_no_historico()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var fila = await mensageria.FilaTesteAsync(TiposEvento.EntregaCriada);
        var pedidoId = await PedidoPagoSemEntregaAsync(cliente, stringConexao);
        (await MudarStatusAsync(operador, pedidoId, "ProntoParaEnvio", "Operação")).StatusCode.Should().Be(HttpStatusCode.OK);
        var entradas = await EntradasHistoricoAsync(stringConexao, pedidoId);

        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, stringConexao);
        await PublicarPagamentoAprovadoAsync(mensageria, pedidoId);

        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(DoPedido(fila, pedidoId).Count > 0), Prazo)).Should().BeTrue();
        (await OrdensAsync(stringConexao, pedidoId)).Should().Be(1);
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("ProntoParaEnvio");
        (await EntradasHistoricoAsync(stringConexao, pedidoId)).Should().Be(entradas);
    }

    [Fact]
    public async Task Pagamento_aprovado_de_pedido_inexistente_vai_para_a_dlq_depois_das_tentativas()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        await ClienteAsync(api);
        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, stringConexao);
        var pedidoId = Guid.CreateVersion7();

        await PublicarPagamentoAprovadoAsync(mensageria, pedidoId);

        (await EsperarNaDlqAsync(mensageria, pedidoId.ToString(), Prazo, FilaEntrega)).Should().NotBeNull("4 execuções com 100, 200 e 400 ms entre elas");
    }

    [Fact]
    public async Task Cancelamento_e_fulfillment_simultaneos_nunca_anunciam_pedido_cancelado_sem_ordem()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);

        for (var rodada = 1; rodada <= 10; rodada++)
        {
            var produtoId = await NovoProdutoAsync(stringConexao, 1);
            var pedidoId = (await CriadoAsync(cliente, (produtoId, 1))).GetProperty("id").GetGuid();
            await Estoque.DisputaEstoqueTestes.ReservarComoABaseAsync(api.Services, pedidoId);
            await AprovarPagamentoAsync(api.Services, pedidoId);

            var cancelamento = MudarStatusAsync(operador, pedidoId, "Cancelado", "Cliente desistiu");
            var entrega = EntregarComoABaseAsync(api.Services, pedidoId);
            await Task.WhenAll(cancelamento, entrega);

            (await cancelamento).StatusCode.Should().Be(HttpStatusCode.OK, $"rodada {rodada}: Processando e ProntoParaEnvio podem ser cancelados");
            (await StatusAsync(stringConexao, pedidoId)).Should().Be("Cancelado", $"rodada {rodada}");
            (await EstoqueAsync(stringConexao, produtoId)).Should().Be(1, $"rodada {rodada}: devolvido uma vez só");
            var ordens = await OrdensAsync(stringConexao, pedidoId);
            ordens.Should().BeLessThanOrEqualTo(1, $"rodada {rodada}");
            (await EntregasCriadasAsync(stringConexao, pedidoId)).Should().Be(ordens, $"rodada {rodada}: evento só com a ordem");
            if (ordens == 1)
            {
                var historico = await BancoDadosTeste.ConsultarAsync<string>(stringConexao,
                    $"SELECT STRING_AGG(StatusNovo, ',') WITHIN GROUP (ORDER BY OcorridoEm, Id) FROM HistoricosStatusPedido WHERE PedidoId = '{pedidoId}'");
                historico.Should().EndWith("ProntoParaEnvio,Cancelado", $"rodada {rodada}: o fulfillment veio antes");
            }
        }
    }

    /// <summary>Aprova o pagamento direto pelo domínio, sem publicar <c>pagamento.aprovado</c>.</summary>
    private static async Task AprovarPagamentoAsync(IServiceProvider servicos, Guid pedidoId)
    {
        await using var escopo = servicos.CreateAsyncScope();
        var armazenamento = escopo.ServiceProvider.GetRequiredService<IArmazenamentoPedidos>();
        var pedido = (await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, Cancelamento))!;
        pedido.AprovarPagamento($"SIM-{Guid.NewGuid():N}", DateTimeOffset.UtcNow);
        await armazenamento.SalvarAsync(Cancelamento);
    }

    /// <summary>
    /// Executa <see cref="CriacaoOrdemEntrega"/> como a base: escopo novo, transação, efeito e
    /// commit; em exceção, desfaz e tenta de novo.
    /// </summary>
    private static async Task EntregarComoABaseAsync(IServiceProvider servicos, Guid pedidoId)
    {
        var evento = EnvelopeEvento.Criar(pedidoId, new PagamentoAprovado
        {
            PedidoId = pedidoId,
            Valor = 10m,
            IdentificadorTransacao = "SIM-0199f6b12c3d7e4f8a5b6c7d8e9f0a12"
        }, DateTimeOffset.UtcNow);
        for (var execucao = 1; ; execucao++)
        {
            await using var escopo = servicos.CreateAsyncScope();
            var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
            await using var transacao = await contexto.Database.BeginTransactionAsync(Cancelamento);
            var consumidor = new CriacaoOrdemEntrega(
                escopo.ServiceProvider.GetRequiredService<IArmazenamentoPedidos>(), TimeProvider.System, NullLogger<CriacaoOrdemEntrega>.Instance);
            try
            {
                await consumidor.ManipularAsync(evento, Cancelamento);
                await contexto.SaveChangesAsync(Cancelamento);
                await transacao.CommitAsync(Cancelamento);
                return;
            }
            catch (Exception) when (execucao < ExecucoesDaBase)
            {
                await transacao.RollbackAsync(Cancelamento);
            }
        }
    }

    private static async Task<Guid> NovoProdutoAsync(string stringConexao, int estoque)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var produto = new Produto($"ENT-{Guid.NewGuid():N}"[..20], "Produto disputado", null, 10m, estoque, true, DateTimeOffset.UtcNow);
        contexto.Produtos.Add(produto);
        await contexto.SaveChangesAsync(Cancelamento);
        return produto.Id;
    }
}
