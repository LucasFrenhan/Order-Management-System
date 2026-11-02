using System.Diagnostics;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.PagamentosHttp;
using static PedidoFulfillment.Testes.Integracao.Observabilidade.CenarioObservabilidade;
using static PedidoFulfillment.Testes.Integracao.Pagamentos.CenarioPagamento;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Observabilidade;

/// <summary>
/// A chamada ao gateway no trace, nas métricas e nos logs (REQ-19, US6, T045; FR-020 a FR-022 e
/// SC-007), com a API simulada real recebendo o <c>traceparent</c>.
/// </summary>
[Collection(nameof(ColecaoObservabilidade))]
public sealed class TelemetriaGatewayPagamentoTestes(MensageriaTeste mensageria)
{
    [Fact]
    public async Task Cobranca_com_falha_e_sucesso_aparece_no_trace_com_um_span_HTTP_por_tentativa()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        await EsperarSemOuvintesAsync();
        using var coletor = new ColetorTelemetria();
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await pagamentos.RoteiroAsync("indisponivel", "aprovar");
        await using var api = Api(stringConexao, coletor);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var worker = await CenarioPagamentoHttp.WorkerAsync(mensageria, stringConexao, pagamentos, telemetria: coletor);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();

        var cobrar = await coletor.EsperarSpanAsync(s => s.DisplayName == "cobrar pagamento" && Equals(s.Atributo("oms.pedido.id"), pedidoId.ToString()), Prazo);
        cobrar.Should().NotBeNull("FR-020: a chamada ao gateway no trace do pedido");
        cobrar!.Atributo("oms.pagamento.operacao").Should().Be("cobranca");
        cobrar.Atributo("oms.resultado").Should().Be("aprovado");
        cobrar.Atributo("oms.pagamento.tentativas").Should().Be(2);
        cobrar.TraceId.Should().Be((await CriacaoAsync(coletor, pedidoId)).TraceId, "SC-007: no mesmo trace da criação do pedido");

        var http = coletor.Spans
            .Where(s => s.ParentSpanId == cobrar.SpanId && s.Source.Name == "System.Net.Http")
            .OrderBy(s => s.StartTimeUtc).ToList();
        http.Should().HaveCount(2, "um span de cliente HTTP por tentativa");
        http.Should().OnlyContain(s => s.Kind == ActivityKind.Client);
        http.Select(s => s.Atributo("http.response.status_code")).Should().Equal(503, 201);
        http.Select(s => s.Atributo("oms.pagamento.tentativa")).Should().Equal(1, 2);
        var propagados = pagamentos.TraceparentsRecebidos.ToList();
        foreach (var span in http)
        {
            propagados.Should().Contain($"00-{span.TraceId}-{span.SpanId}-01", "o contexto do trace chega à API simulada");
        }

        coletor.Soma("oms.pagamentos.chamadas", ("oms.pagamento.operacao", "cobranca"), ("oms.resultado", "aprovado")).Should().BeGreaterThanOrEqualTo(1);
        coletor.Medicoes.Should().Contain(m => m.Nome == "oms.pagamentos.duracao" && m.Unidade == "s");
        coletor.LerGauges();
        // O gauge é do processo e outros testes em paralelo mexem no circuito: basta ele existir.
        coletor.Medicoes.Should().Contain(m => m.Nome == "oms.pagamentos.circuito");

        var tudo = coletor.Spans.SelectMany(s => s.TagObjects.Select(t => t.Value?.ToString() ?? ""))
            .Concat(coletor.Logs.SelectMany(l => l.Atributos.Values.Select(v => v ?? "").Append(l.Mensagem ?? "")));
        tudo.Should().NotContain(t => t.Contains(FabricaPagamentos.ChaveApiTeste, StringComparison.Ordinal), "FR-002: a chave nunca vai para a telemetria");
        coletor.Logs.Should().Contain(l => l.Evento.Id == 7013 && l.TraceId == cobrar.TraceId, "o log da falha passageira fica no trace do pedido");
    }
}
