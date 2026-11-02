using System.Net;
using FluentAssertions;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Integracao.Estornos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Reconciliacao;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pagamentos.CenarioPagamento;
using static PedidoFulfillment.Testes.Integracao.PagamentosHttp.CenarioPagamentoHttp;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.PagamentosHttp;

/// <summary>
/// Com a API fora do ar, o circuito abre e o consumo de pagamento pausa (REQ-19, US5, T040; FR-016,
/// FR-016a e SC-004): sem DLQ, sem nova tentativa contada, prontidão saudável e retomada sozinha.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class CircuitoGatewayTestes(MensageriaTeste mensageria)
{
    private static readonly string FilaMortasPagamento = TopologiaMensageria.FilaMortas(FilaPagamento);

    [Fact]
    public async Task API_fora_do_ar_abre_o_circuito_pausa_o_consumo_e_retoma_sozinho()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await pagamentos.ModoAsync("indisponivel");
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var logs = new CapturaLogs();
        // O Worker completo: a prontidão confere todos os consumidores conhecidos.
        await using var worker = await WorkerAsync(mensageria, stringConexao, pagamentos, logs, comEntrega: true);

        var pedidos = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            pedidos.Add((await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid());
        }

        (await EsperarLogAsync(logs, 7016)).Should().BeTrue("FR-016: o circuito abre");
        (await EsperarLogAsync(logs, 5020)).Should().BeTrue("FR-016a: o consumo de pagamento pausa");
        var antes = await pagamentos.ChamadasRecebidasAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(600), TestContext.Current.CancellationToken);
        (await pagamentos.ChamadasRecebidasAsync() - antes).Should().BeLessThanOrEqualTo(1, "SC-004: com o circuito aberto, a API para de receber chamadas");
        using (var saude = worker.CreateClient())
        {
            (await saude.GetAsync("/health/pronto", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK, "pausado conta como pronto");
        }

        foreach (var pedidoId in pedidos)
        {
            (await ReservadoAsync(stringConexao, pedidoId)).Should().BeTrue("os outros consumidores continuam");
        }

        await pagamentos.DesfazerModoAsync();

        foreach (var pedidoId in pedidos)
        {
            (await MensageriaTeste.EsperarAteAsync(async () => await PagamentosAsync(stringConexao, pedidoId) == 1
                && await SituacaoAsync(stringConexao, pedidoId) == "Aprovado", Prazo)).Should().BeTrue("a retomada cobra os que esperavam");
            await NoMaximoUmaEfetivacaoAsync(pagamentos, pedidoId);
        }

        (await EsperarLogAsync(logs, 5021)).Should().BeTrue();
        (await EsperarLogAsync(logs, 7018)).Should().BeTrue();
        Contar(logs, 5004).Should().Be(0, "SC-004: nada vai para a DLQ por causa da queda");
        (await mensageria.MensagensNaFilaAsync(FilaMortasPagamento) ?? 0).Should().Be(0);
    }

    [Fact]
    public async Task Com_pouco_movimento_um_unico_pedido_tambem_pausa_sem_chegar_a_DLQ()
    {
        // Analyze C3: com MinimoChamadas 4 e 3 tentativas por entrega, a segunda entrega abre o circuito.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await pagamentos.ModoAsync("indisponivel");
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var logs = new CapturaLogs();
        await using var worker = await WorkerAsync(mensageria, stringConexao, pagamentos, logs);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        (await EsperarLogAsync(logs, 5020)).Should().BeTrue();
        await pagamentos.DesfazerModoAsync();
        (await EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
        Contar(logs, 5004).Should().Be(0);
        (await CobrancaAsync(pagamentos, pedidoId))!.Efetivacoes.Should().Be(1);
    }

    [Fact]
    public async Task Com_o_circuito_aberto_o_estorno_fica_adiado_sem_contar_tentativa()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        Guid pedidoId;
        await using (var pagador = await WorkerAsync(mensageria, stringConexao, pagamentos))
        {
            pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
            (await EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
        }

        await CenarioEstorno.CancelarComoOperacaoAsync(await OperadorAsync(api), pedidoId);
        await pagamentos.ModoAsync("indisponivel");
        var logs = new CapturaLogs();
        await using var worker = CenarioEstorno.WorkerEstornando(mensageria, stringConexao, logs, apiPagamentos: pagamentos);
        worker.CreateClient().Dispose();

        (await EsperarLogAsync(logs, 7020)).Should().BeTrue("FR-016a: o estorno fica adiado com o circuito aberto");
        var tentativas = await CenarioEstorno.TentativasAsync(stringConexao, pedidoId);
        (await EsperarLogAsync(logs, 7020, quantidade: 2)).Should().BeTrue();
        (await CenarioEstorno.TentativasAsync(stringConexao, pedidoId)).Should().Be(tentativas, "a rodada adiada não conta tentativa");
        (await CenarioEstorno.SituacaoAsync(stringConexao, pedidoId)).Should().Be("EstornoPendente");

        await pagamentos.DesfazerModoAsync();

        (await CenarioEstorno.EsperarSituacaoAsync(stringConexao, pedidoId, "Estornado", Prazo)).Should().BeTrue();
        (await EstornoAsync(pagamentos, pedidoId))!.Efetivacoes.Should().Be(1);
    }

    [Fact]
    public async Task Com_o_circuito_aberto_a_reconciliacao_conta_falha_do_ciclo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await pagamentos.ModoAsync("indisponivel");
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await CenarioReconciliacao.ParadoNoPagamentoAsync(mensageria, stringConexao, cliente);
        await CenarioReconciliacao.EnvelhecerAsync(stringConexao, pedidoId, TimeSpan.FromHours(1));
        var logs = new CapturaLogs();

        try
        {
            await using var worker = CenarioReconciliacao.WorkerReconciliando(mensageria, stringConexao, logs, apiPagamentos: pagamentos);
            worker.CreateClient().Dispose();

            (await EsperarLogAsync(logs, 7016)).Should().BeTrue();
            (await EsperarLogAsync(logs, 9003, quantidade: 3)).Should().BeTrue("REQ-12: falha do ciclo, como com o gateway fora do ar");
            logs.Registros.Where(r => r.Evento.Id == 9003).Should().Contain(r => r.Texto.Contains("GatewayPagamentoIndisponivelException", StringComparison.Ordinal));
            (await PagamentosAsync(stringConexao, pedidoId)).Should().Be(0);
        }
        finally
        {
            await CenarioReconciliacao.LimparFilasAsync(mensageria);
        }
    }
}
