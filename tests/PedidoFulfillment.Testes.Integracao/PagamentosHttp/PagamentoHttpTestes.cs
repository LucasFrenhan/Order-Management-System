using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Reconciliacao;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pagamentos.CenarioPagamento;
using static PedidoFulfillment.Testes.Integracao.PagamentosHttp.CenarioPagamentoHttp;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.PagamentosHttp;

/// <summary>
/// O pagamento pela rede, com a API simulada real, SQL Server e RabbitMQ (REQ-19, US1, T018;
/// FR-009, FR-014 e FR-024).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class PagamentoHttpTestes(MensageriaTeste mensageria)
{
    [Fact]
    public async Task Pedido_e_cobrado_pela_API_com_o_valor_e_a_chave_do_pedido_e_vai_para_processando()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var worker = await WorkerAsync(mensageria, stringConexao, pagamentos);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2), (Cat002, 1))).GetProperty("id").GetGuid();

        (await EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
        var transacao = await TransacaoAsync(stringConexao, pedidoId);
        transacao.Should().MatchRegex("^PAG-[0-9a-f]{32}$", "o identificador vem da API simulada");
        (await CobrancaAsync(pagamentos, pedidoId)).Should().Be(new PagamentosSimulados.Simulacao.RespostaOperacaoControle("aprovada", 1, 1));
        using var consulta = pagamentos.ClienteApi();
        var guardada = await ApiPagamentosSimuladaTestes.CorpoAsync(await consulta.GetAsync($"v1/cobrancas/{pedidoId}", TestContext.Current.CancellationToken));
        guardada.GetProperty("identificador").GetString().Should().Be(transacao);
        guardada.GetProperty("valor").GetDecimal().Should().Be(await ValorTotalAsync(stringConexao, pedidoId));
    }

    [Fact]
    public async Task Recusa_da_API_rejeita_o_pedido_e_devolve_o_estoque()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await pagamentos.ModoAsync("recusar");
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var worker = await WorkerAsync(mensageria, stringConexao, pagamentos);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2))).GetProperty("id").GetGuid();

        (await EsperarStatusAsync(stringConexao, pedidoId, "PagamentoRejeitado", Prazo)).Should().BeTrue();
        (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("Recusado");
        (await ReservadoAsync(stringConexao, pedidoId)).Should().BeFalse("o estoque volta, como hoje (REQ-09)");
        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(50);
        (await CobrancaAsync(pagamentos, pedidoId))!.Efetivacoes.Should().Be(1);
    }

    [Fact]
    public async Task Credencial_errada_nao_grava_nada_e_vai_para_o_log_como_erro_permanente()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar(new Dictionary<string, string?> { ["Seguranca:ChaveApi"] = "outra-chave-da-api-123" });
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var logs = new CapturaLogs();
        await using var worker = await WorkerAsync(mensageria, stringConexao, pagamentos, logs);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        (await EsperarLogAsync(logs, 7015)).Should().BeTrue("FR-014: erro de configuração no log");
        (await EsperarLogAsync(logs, 5004)).Should().BeTrue("decisão do usuário: credencial errada segue para a DLQ, sem pausar");
        (await PagamentosAsync(stringConexao, pedidoId)).Should().Be(0);
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("Pendente");
        Contar(logs, 5020).Should().Be(0, "erro permanente não pausa o consumo");
        logs.Textos.Should().NotContain(t => t.Contains(FabricaPagamentos.ChaveApiTeste, StringComparison.Ordinal), "FR-002");
        (await CobrancaAsync(pagamentos, pedidoId)).Should().BeNull();
    }

    [Fact]
    public async Task A_reconciliacao_cobra_pela_API_com_a_mesma_chave_do_pedido()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await CenarioReconciliacao.ParadoNoPagamentoAsync(mensageria, stringConexao, cliente);
        await CenarioReconciliacao.EnvelhecerAsync(stringConexao, pedidoId, TimeSpan.FromHours(1));

        try
        {
            await using var worker = CenarioReconciliacao.WorkerReconciliando(mensageria, stringConexao, apiPagamentos: pagamentos);
            worker.CreateClient().Dispose();

            (await EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
            (await TransacaoAsync(stringConexao, pedidoId)).Should().StartWith("PAG-");
            (await CobrancaAsync(pagamentos, pedidoId))!.Efetivacoes.Should().Be(1);
        }
        finally
        {
            await CenarioReconciliacao.LimparFilasAsync(mensageria);
        }
    }
}
