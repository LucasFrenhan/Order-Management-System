using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Estornos.CenarioEstorno;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Estornos;

/// <summary>
/// Cancelamento pela API de pedidos pagos, com SQL Server e RabbitMQ reais (REQ-13, US1 e US2):
/// a resposta não muda, e o estorno fica pendente na mesma gravação.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class CancelamentoPedidoPagoTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Operacao_cancela_pedido_pago_com_estoque_devolvido_e_estorno_pendente_sem_evento_novo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var estoqueAntes = (await EstoqueAsync(stringConexao, Cat001), await EstoqueAsync(stringConexao, Cat002));
        var pedidoId = await PedidoPagoAsync(mensageria, stringConexao, cliente);
        var eventosAntes = await EventosSaidaAsync(stringConexao);

        var resposta = await MudarStatusAsync(await OperadorAsync(api), pedidoId, "Cancelado", "Cliente desistiu depois do pagamento.");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var corpo = await CorpoAsync(resposta);
        corpo.GetProperty("status").GetString().Should().Be("Cancelado");
        corpo.GetProperty("historico").EnumerateArray().Last().GetProperty("motivo").GetString().Should().Be("Cliente desistiu depois do pagamento.");
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("Cancelado");
        (await ReservadoAsync(stringConexao, pedidoId)).Should().BeFalse();
        (await EstoqueAsync(stringConexao, Cat001), await EstoqueAsync(stringConexao, Cat002)).Should().Be(estoqueAntes);
        (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("EstornoPendente");
        (await TentativasAsync(stringConexao, pedidoId)).Should().Be(0);
        (await EventosSaidaAsync(stringConexao)).Should().Be(eventosAntes, "REQ-13, FR-017: o cancelamento não publica nada");
    }

    [Fact]
    public async Task Pedido_pendente_sem_pagamento_e_cancelado_sem_estorno()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        (await MudarStatusAsync(await OperadorAsync(api), pedidoId, "Cancelado", "Sem estoque.")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Pagamentos.CenarioPagamento.PagamentosAsync(stringConexao, pedidoId)).Should().Be(0);
    }

    [Fact]
    public async Task Pedido_com_pagamento_rejeitado_continua_respondendo_409_sem_estorno()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        Guid pedidoId;
        await using (await Pagamentos.CenarioPagamento.WorkerProntoAsync(mensageria, stringConexao, taxaAprovacao: 0))
        {
            pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
            (await Pagamentos.CenarioPagamento.EsperarStatusAsync(stringConexao, pedidoId, "PagamentoRejeitado", Prazo)).Should().BeTrue();
        }

        var resposta = await MudarStatusAsync(await OperadorAsync(api), pedidoId, "Cancelado", "Tentativa.");

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(resposta)).Should().Be("PEDIDO_TRANSICAO_INVALIDA");
        (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("Recusado");
    }

    [Fact]
    public async Task Cliente_continua_recebendo_403_ao_cancelar_o_proprio_pedido_pago()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await PedidoPagoAsync(mensageria, stringConexao, cliente);

        var resposta = await MudarStatusAsync(cliente, pedidoId, "Cancelado", "Desisti.");

        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("Processando");
        (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("Aprovado");
    }

    [Fact]
    public async Task A_consulta_do_pedido_cancelado_nao_mostra_pagamento_nem_estorno()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await PedidoPagoAsync(mensageria, stringConexao, cliente);
        await CancelarComoOperacaoAsync(await OperadorAsync(api), pedidoId);

        var corpo = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/pedidos/{pedidoId}", Cancelamento);

        var texto = corpo.GetRawText();
        texto.Should().NotContainAny("estorno", "Estorno", "pagamento\"", "EstornoPendente");
        corpo.TryGetProperty("pagamento", out _).Should().BeFalse("clarify Q2: as situações de estorno são internas");
    }
}
