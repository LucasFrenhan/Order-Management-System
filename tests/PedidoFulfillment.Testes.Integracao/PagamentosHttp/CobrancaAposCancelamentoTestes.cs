using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Estornos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pagamentos.CenarioPagamento;
using static PedidoFulfillment.Testes.Integracao.PagamentosHttp.CenarioPagamentoHttp;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.PagamentosHttp;

/// <summary>
/// Cobrança efetivada sem resposta e pedido cancelado antes da reentrega (REQ-19, US4 cenário 3,
/// T035; FR-015 e R-08): o OMS consulta pela chave e registra o pagamento com estorno pendente.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class CobrancaAposCancelamentoTestes(MensageriaTeste mensageria)
{
    private static readonly Dictionary<string, string?> ComEstornos = new()
    {
        ["Estornos:Habilitado"] = "true",
        ["Estornos:IntervaloSegundos"] = "1"
    };

    /// <summary>
    /// Espera a entrega em andamento terminar sem resultado (nova tentativa da base ou pausa): se o
    /// modo fosse desfeito no meio dela, a última tentativa aprovaria e o caminho seria o do REQ-13 R-07.
    /// </summary>
    private static async Task EsperarFimDaEntregaSemResultadoAsync(CapturaLogs logs)
    {
        var antes = Contar(logs, 5003) + Contar(logs, 5020);
        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(Contar(logs, 5003) + Contar(logs, 5020) > antes), Prazo))
            .Should().BeTrue("a entrega em andamento termina sem resultado");
    }

    [Fact]
    public async Task Cobranca_sem_resposta_em_pedido_cancelado_e_consultada_e_estornada()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        // A primeira tentativa efetiva sem responder; as seguintes ficam sem resposta (research R-03:
        // a falha sobre a chave concluída não apaga a cobrança).
        await pagamentos.RoteiroAsync("processar-sem-responder");
        await pagamentos.ModoAsync("sem-resposta");
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var logs = new CapturaLogs();
        await using var worker = await WorkerAsync(mensageria, stringConexao, pagamentos, logs, ComEstornos);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2))).GetProperty("id").GetGuid();
        (await MensageriaTeste.EsperarAteAsync(async () => (await CobrancaAsync(pagamentos, pedidoId))?.Efetivacoes == 1, Prazo)).Should().BeTrue();
        await CenarioEstorno.CancelarComoOperacaoAsync(await OperadorAsync(api), pedidoId);
        (await PagamentosAsync(stringConexao, pedidoId)).Should().Be(0, "nada gravado sem resultado (FR-013)");

        await EsperarFimDaEntregaSemResultadoAsync(logs);
        await pagamentos.DesfazerModoAsync();

        (await EsperarLogAsync(logs, 7019)).Should().BeTrue("FR-015: a reentrega encontra o pedido cancelado sem pagamento e consulta");
        logs.Registros.First(r => r.Evento.Id == 7019).Texto.Should().Contain("Aprovada");
        (await CenarioEstorno.EsperarSituacaoAsync(stringConexao, pedidoId, "Estornado", Prazo)).Should().BeTrue("SC-005: registrado com o estorno pendente e estornado");
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("Cancelado");
        using var consulta = pagamentos.ClienteApi();
        var guardada = await ApiPagamentosSimuladaTestes.CorpoAsync(await consulta.GetAsync($"v1/cobrancas/{pedidoId}", TestContext.Current.CancellationToken));
        (await TransacaoAsync(stringConexao, pedidoId)).Should().Be(guardada.GetProperty("identificador").GetString());
        await NoMaximoUmaEfetivacaoAsync(pagamentos, pedidoId);
        (await EstornoAsync(pagamentos, pedidoId))!.Efetivacoes.Should().Be(1);
    }

    [Fact]
    public async Task Sem_cobranca_na_API_o_pedido_cancelado_fica_sem_pagamento()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await pagamentos.ModoAsync("sem-resposta");
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var logs = new CapturaLogs();
        await using var worker = await WorkerAsync(mensageria, stringConexao, pagamentos, logs);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await EsperarLogAsync(logs, 7014)).Should().BeTrue();
        await CenarioEstorno.CancelarComoOperacaoAsync(await OperadorAsync(api), pedidoId);
        await EsperarFimDaEntregaSemResultadoAsync(logs);
        await pagamentos.DesfazerModoAsync();

        (await EsperarLogAsync(logs, 7019)).Should().BeTrue();
        logs.Registros.First(r => r.Evento.Id == 7019).Texto.Should().Contain("NaoEncontrada");
        (await PagamentosAsync(stringConexao, pedidoId)).Should().Be(0);
        (await CobrancaAsync(pagamentos, pedidoId)).Should().BeNull("consultar não cobra");
    }
}
