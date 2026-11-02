using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Estornos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pagamentos.CenarioPagamento;
using static PedidoFulfillment.Testes.Integracao.PagamentosHttp.CenarioPagamentoHttp;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.PagamentosHttp;

/// <summary>
/// O estorno pela rede (REQ-19, US1 cenário 2 e US3 cenário 7, T019 e T029; REQ-13 R-04):
/// cobrança e estorno na API simulada real, com a mesma chave do pedido.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class EstornoHttpTestes(MensageriaTeste mensageria)
{
    /// <summary>Pedido cobrado pela API e cancelado pela operação, com o Worker parado.</summary>
    private async Task<(string StringConexao, Guid PedidoId, string Transacao)> PagoECanceladoAsync(FabricaPagamentos pagamentos)
    {
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        Guid pedidoId;
        await using (var worker = await WorkerAsync(mensageria, stringConexao, pagamentos))
        {
            pedidoId = (await CriadoAsync(cliente, (Cat001, 2))).GetProperty("id").GetGuid();
            (await EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
        }

        await CenarioEstorno.CancelarComoOperacaoAsync(await OperadorAsync(api), pedidoId);
        return (stringConexao, pedidoId, await TransacaoAsync(stringConexao, pedidoId));
    }

    [Fact]
    public async Task Pedido_pago_e_cancelado_e_estornado_pela_API_com_o_identificador_EST()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var pagamentos = FabricaPagamentos.Iniciar();
        var (stringConexao, pedidoId, _) = await PagoECanceladoAsync(pagamentos);

        await using var worker = CenarioEstorno.WorkerEstornando(mensageria, stringConexao, apiPagamentos: pagamentos);
        worker.CreateClient().Dispose();

        (await CenarioEstorno.EsperarSituacaoAsync(stringConexao, pedidoId, "Estornado", Prazo)).Should().BeTrue();
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT IdentificadorEstorno FROM Pagamentos WHERE PedidoId = '{pedidoId}'"))
            .Should().MatchRegex("^EST-[0-9a-f]{32}$");
        (await EstornoAsync(pagamentos, pedidoId)).Should().Be(new PagamentosSimulados.Simulacao.RespostaOperacaoControle("confirmado", 1, 1));
        await NoMaximoUmaEfetivacaoAsync(pagamentos, pedidoId);
    }

    [Fact]
    public async Task Estorno_com_falha_passageira_e_repetido_na_mesma_chamada_e_confirmado_uma_vez()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var pagamentos = FabricaPagamentos.Iniciar();
        var (stringConexao, pedidoId, _) = await PagoECanceladoAsync(pagamentos);
        await pagamentos.RoteiroAsync("indisponivel", "aprovar");
        var logs = new CapturaLogs();

        await using var worker = CenarioEstorno.WorkerEstornando(mensageria, stringConexao, logs, apiPagamentos: pagamentos);
        worker.CreateClient().Dispose();

        (await CenarioEstorno.EsperarSituacaoAsync(stringConexao, pedidoId, "Estornado", Prazo)).Should().BeTrue();
        (await CenarioEstorno.TentativasAsync(stringConexao, pedidoId)).Should().Be(0, "a falha foi resolvida dentro da chamada");
        (await EstornoAsync(pagamentos, pedidoId))!.Efetivacoes.Should().Be(1);
        Contar(logs, 7013).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Estorno_sem_resultado_depois_das_tentativas_continua_pendente_com_a_tentativa_contada()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var pagamentos = FabricaPagamentos.Iniciar();
        var (stringConexao, pedidoId, _) = await PagoECanceladoAsync(pagamentos);
        await pagamentos.RoteiroAsync("erro-interno", "erro-interno", "erro-interno");

        await using var worker = CenarioEstorno.WorkerEstornando(mensageria, stringConexao, apiPagamentos: pagamentos);
        worker.CreateClient().Dispose();

        (await MensageriaTeste.EsperarAteAsync(async () => await CenarioEstorno.TentativasAsync(stringConexao, pedidoId) >= 1, Prazo))
            .Should().BeTrue("REQ-13: a falha conta a tentativa e a rodada seguinte tenta de novo");
        (await CenarioEstorno.EsperarSituacaoAsync(stringConexao, pedidoId, "Estornado", Prazo)).Should().BeTrue();
        (await EstornoAsync(pagamentos, pedidoId))!.Efetivacoes.Should().Be(1);
    }
}
