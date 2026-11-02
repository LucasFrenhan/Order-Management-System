using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Estornos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pagamentos.CenarioPagamento;
using static PedidoFulfillment.Testes.Integracao.PagamentosHttp.CenarioPagamentoHttp;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.PagamentosHttp;

/// <summary>
/// Resultado incerto: a API efetiva e a resposta não chega (REQ-19, US4, T034; FR-013, FR-017 e
/// SC-002/SC-005), com a API simulada real, banco e broker.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class ResultadoIncertoHttpTestes(MensageriaTeste mensageria)
{
    [Fact]
    public async Task Cobranca_efetivada_sem_resposta_e_resolvida_pela_mesma_chave_com_o_identificador_original()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await pagamentos.RoteiroAsync("processar-sem-responder");
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var logs = new CapturaLogs();
        await using var worker = await WorkerAsync(mensageria, stringConexao, pagamentos, logs);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        (await EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
        (await EsperarLogAsync(logs, 7014)).Should().BeTrue();
        using var consulta = pagamentos.ClienteApi();
        var guardada = await ApiPagamentosSimuladaTestes.CorpoAsync(await consulta.GetAsync($"v1/cobrancas/{pedidoId}", TestContext.Current.CancellationToken));
        (await TransacaoAsync(stringConexao, pedidoId)).Should().Be(guardada.GetProperty("identificador").GetString(), "SC-005: o identificador original");
        (await CobrancaAsync(pagamentos, pedidoId)).Should().Be(new PagamentosSimulados.Simulacao.RespostaOperacaoControle("aprovada", 1, 2));
    }

    [Fact]
    public async Task Depois_de_reiniciar_o_Worker_a_API_ainda_reconhece_a_chave()
    {
        // US4 cenário 2: as chaves ficam na API, não no Worker.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await pagamentos.RoteiroAsync("processar-sem-responder", "sem-resposta", "sem-resposta", "sem-resposta", "sem-resposta", "sem-resposta");
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        Guid pedidoId;
        await using (var primeiro = await WorkerAsync(mensageria, stringConexao, pagamentos))
        {
            pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
            (await MensageriaTeste.EsperarAteAsync(async () => (await CobrancaAsync(pagamentos, pedidoId))?.Efetivacoes == 1, Prazo))
                .Should().BeTrue("a API efetivou a cobrança, sem a resposta chegar ao Worker");
        }

        (await PagamentosAsync(stringConexao, pedidoId)).Should().Be(0, "nada é gravado sem resultado (FR-013)");
        await pagamentos.EsvaziarRoteiroAsync();

        await using var segundo = await WorkerAsync(mensageria, stringConexao, pagamentos, limparFilas: false);

        (await EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
        using var consulta = pagamentos.ClienteApi();
        var guardada = await ApiPagamentosSimuladaTestes.CorpoAsync(await consulta.GetAsync($"v1/cobrancas/{pedidoId}", TestContext.Current.CancellationToken));
        (await TransacaoAsync(stringConexao, pedidoId)).Should().Be(guardada.GetProperty("identificador").GetString());
        (await CobrancaAsync(pagamentos, pedidoId))!.Efetivacoes.Should().Be(1);
    }

    [Fact]
    public async Task Estorno_efetivado_sem_resposta_e_confirmado_uma_unica_vez()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        Guid pedidoId;
        await using (var worker = await WorkerAsync(mensageria, stringConexao, pagamentos))
        {
            pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
            (await EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
        }

        await CenarioEstorno.CancelarComoOperacaoAsync(await OperadorAsync(api), pedidoId);
        await pagamentos.RoteiroAsync("processar-sem-responder");
        var logs = new CapturaLogs();

        await using var estornando = CenarioEstorno.WorkerEstornando(mensageria, stringConexao, logs, apiPagamentos: pagamentos);
        estornando.CreateClient().Dispose();

        (await CenarioEstorno.EsperarSituacaoAsync(stringConexao, pedidoId, "Estornado", Prazo)).Should().BeTrue();
        (await EsperarLogAsync(logs, 7014)).Should().BeTrue();
        (await EstornoAsync(pagamentos, pedidoId)).Should().Be(new PagamentosSimulados.Simulacao.RespostaOperacaoControle("confirmado", 1, 2));
        Contar(logs, 7006).Should().Be(1, "estornado uma única vez");
    }

    [Fact]
    public async Task Duas_instancias_do_Worker_cobrando_o_mesmo_pedido_efetivam_uma_unica_cobranca()
    {
        // SC-002, FR-017: reentrega e reconciliação ao mesmo tempo, em instâncias diferentes.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await pagamentos.AlterarComportamentoAsync(new { latenciaMinimaMs = 150, latenciaMaximaMs = 150 });
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        // Reservado e ainda sem cobrança: os dois eventos chegam com o pedido apto nas duas instâncias.
        var pedidoId = await Reconciliacao.CenarioReconciliacao.ParadoNoPagamentoAsync(mensageria, stringConexao, cliente);
        await using var primeiro = await WorkerAsync(mensageria, stringConexao, pagamentos);
        await using var segundo = await WorkerAsync(mensageria, stringConexao, pagamentos, limparFilas: false);

        await Task.WhenAll(PublicarEstoqueReservadoAsync(mensageria, pedidoId), PublicarEstoqueReservadoAsync(mensageria, pedidoId));

        (await EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
        // As duas mensagens terminam (cobrada ou ignorada) antes da conferência.
        (await MensageriaTeste.EsperarAteAsync(async () => await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            "SELECT COUNT(*) FROM EventosProcessados WHERE Consumidor = 'pagamento'") >= 2, Prazo)).Should().BeTrue();
        (await CobrancaAsync(pagamentos, pedidoId))!.Efetivacoes.Should().Be(1);
        (await PagamentosAsync(stringConexao, pedidoId)).Should().Be(1);
    }
}
