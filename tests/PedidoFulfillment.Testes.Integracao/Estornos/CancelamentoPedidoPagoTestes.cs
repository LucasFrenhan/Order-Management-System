using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
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
    public async Task Operacao_cancela_pedido_pago_com_estoque_devolvido_estorno_pendente_e_pedido_cancelado()
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
        // REQ-15, US1 cenário 2: um único evento novo, o pedido.cancelado da mesma gravação.
        (await EventosSaidaAsync(stringConexao)).Should().Be(eventosAntes + 1);
        var cancelado = await EventosOutbox.CanceladoAsync(stringConexao, pedidoId);
        (cancelado.Motivo, cancelado.EtapaEsgotada).Should().Be((MotivoCancelamento.Operacao, (EtapaEsgotada?)null));
        (cancelado.EstoqueDevolvido, cancelado.EstornoPendente, cancelado.OrdemEntregaCancelada).Should().Be((true, true, false));
    }

    [Fact]
    public async Task Cancelar_pedido_pronto_para_envio_cancela_a_ordem_de_entrega()
    {
        // US2 cenários 1 e 2.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        var logs = new CapturaLogs();
        await using var api = Api(stringConexao, logs);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await PedidoProntoParaEnvioAsync(mensageria, stringConexao, cliente);

        var resposta = await MudarStatusAsync(await OperadorAsync(api), pedidoId, "Cancelado", "Cliente desistiu antes do envio.");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var entrega = (await CorpoAsync(resposta)).GetProperty("entrega");
        entrega.GetProperty("canceladaEm").ValueKind.Should().Be(JsonValueKind.String);
        entrega.GetProperty("transportadora").ValueKind.Should().Be(JsonValueKind.Null);
        entrega.GetProperty("codigoRastreio").ValueKind.Should().Be(JsonValueKind.Null);
        entrega.GetProperty("despachadoEm").ValueKind.Should().Be(JsonValueKind.Null);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM OrdensEntrega WHERE PedidoId = '{pedidoId}' AND CanceladaEm IS NOT NULL")).Should().Be(1);
        (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("EstornoPendente");
        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 8004);
        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 7005);

        var consulta = await cliente.GetFromJsonAsync<JsonElement>($"/api/v1/pedidos/{pedidoId}", Cancelamento);
        consulta.GetProperty("entrega").GetProperty("canceladaEm").GetDateTimeOffset()
            .Should().Be(entrega.GetProperty("canceladaEm").GetDateTimeOffset());

        // REQ-15, US1 cenário 3.
        var cancelado = await EventosOutbox.CanceladoAsync(stringConexao, pedidoId);
        cancelado.Motivo.Should().Be(MotivoCancelamento.Operacao);
        (cancelado.EstoqueDevolvido, cancelado.EstornoPendente, cancelado.OrdemEntregaCancelada).Should().Be((true, true, true));
        cancelado.CanceladoEm.Should().Be(entrega.GetProperty("canceladaEm").GetDateTimeOffset());
    }

    [Fact]
    public async Task Rastreio_de_pedido_cancelado_com_ordem_cancelada_responde_409()
    {
        // US2 cenário 3.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await PedidoProntoParaEnvioAsync(mensageria, stringConexao, cliente);
        var operador = await OperadorAsync(api);
        await CancelarComoOperacaoAsync(operador, pedidoId);

        var resposta = await RegistrarRastreioAsync(operador, pedidoId, "Correios", "BR123456789BR");

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(resposta)).Should().Be("PEDIDO_TRANSICAO_INVALIDA");
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM OrdensEntrega WHERE PedidoId = '{pedidoId}' AND Transportadora IS NULL AND DespachadoEm IS NULL")).Should().Be(1);
    }

    [Fact]
    public async Task Pedido_despachado_continua_sem_poder_ser_cancelado_e_nada_e_estornado()
    {
        // US2 cenário 4.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await PedidoProntoParaEnvioAsync(mensageria, stringConexao, cliente);
        var operador = await OperadorAsync(api);
        (await RegistrarRastreioAsync(operador, pedidoId, "Correios", "BR123456789BR")).StatusCode.Should().Be(HttpStatusCode.OK);

        var resposta = await MudarStatusAsync(operador, pedidoId, "Cancelado", "Tarde demais.");

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CorpoAsync(resposta)).GetProperty("detail").GetString().Should().Be("O pedido está em Despachado e não pode passar para Cancelado.");
        (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("Aprovado");
        (await EventosOutbox.ListarAsync<PedidoCancelado>(stringConexao, pedidoId)).Should().BeEmpty("REQ-15: a recusa não gera evento");
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
        var cancelado = await EventosOutbox.CanceladoAsync(stringConexao, pedidoId);
        (cancelado.Motivo, cancelado.EstornoPendente).Should().Be((MotivoCancelamento.Operacao, false));
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
        // REQ-15, FR-005: PagamentoRejeitado não é cancelamento; só o pagamento.recusado saiu.
        (await EventosOutbox.ListarAsync<PagamentoRecusado>(stringConexao, pedidoId)).Should().ContainSingle();
        (await EventosOutbox.ListarAsync<PedidoCancelado>(stringConexao, pedidoId)).Should().BeEmpty();
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
        (await EventosOutbox.ListarAsync<PedidoCancelado>(stringConexao, pedidoId)).Should().BeEmpty("REQ-15: a recusa não gera evento");
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
