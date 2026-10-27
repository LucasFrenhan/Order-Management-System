using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Entregas;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Testes.Integracao.Estoque;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Administracao.CenarioReenvio;
using static PedidoFulfillment.Testes.Integracao.Observabilidade.CenarioObservabilidade;

namespace PedidoFulfillment.Testes.Integracao.Observabilidade;

/// <summary>
/// User Story 2 do REQ-16 (T017): a mensagem reenviada da DLQ continua o trace de origem dela,
/// com link para a execução do reenvio, que tem link para a requisição do Admin.
/// </summary>
[Collection(nameof(ColecaoObservabilidade))]
public sealed class TraceReenvioDlqTestes(MensageriaTeste mensageria)
{
    [Fact]
    public async Task Mensagem_reenviada_continua_o_trace_de_origem_e_liga_a_execucao_ao_pedido_do_admin()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await PrepararFilasAsync(mensageria);
        // Publicada antes de existir qualquer provedor: sem traceparent.
        var semOrigem = await NaDlqAsync(mensageria);

        using var coletor = new ColetorTelemetria();
        await using var api = Api(stringConexao, coletor);
        var admin = await Pedidos.CenarioPedidos.AdminAsync(api);
        // Com a telemetria ligada, a publicação do teste vira o trace de origem da mensagem.
        var comOrigem = await NaDlqAsync(mensageria);
        var bloqueada = await NaDlqAsync(mensageria, reenvios: 3);
        var origem = (await coletor.EsperarSpanAsync(s => s.Source.Name == "RabbitMQ.Client.Publisher" && Equals(s.Atributo("messaging.message.id"), comOrigem), CenarioObservabilidade.Prazo))!;

        var resposta = await admin.PostAsJsonAsync($"/api/v1/admin/dlq/{Consumidor}/reenvios", new { }, TestContext.Current.CancellationToken);
        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var pedidoId = (await resposta.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
        var registro = (await coletor.EsperarSpanAsync(s => s.EhRequisicao("POST", "/api/v1/admin/dlq/{consumidor}/reenvios"), CenarioObservabilidade.Prazo))!;

        await using (var worker = Worker(stringConexao, coletor))
        {
            worker.CreateClient().Dispose();
            (await EsperarSituacaoAsync(stringConexao, pedidoId, SituacaoReenvioDlq.Concluido)).Should().BeTrue();
            (await coletor.EsperarSpanAsync(s => EhEntregaDe(s, comOrigem), CenarioObservabilidade.Prazo)).Should().NotBeNull("a cópia é consumida");
        }

        var execucao = coletor.Span(s => s.DisplayName == $"reenviar_dlq {Consumidor}" && Equals(s.Atributo("oms.reenvio.pedido_id"), pedidoId.ToString()));
        execucao.Should().NotBeNull();
        execucao!.ParentSpanId.Should().Be(default(ActivitySpanId), "cada execução é um trace próprio");
        execucao.Links.Select(l => l.Context.TraceId).Should().Equal([registro.TraceId], "ligada à requisição do Admin");
        (execucao.Atributo("oms.reenvio.reenviadas"), execucao.Atributo("oms.reenvio.bloqueadas"), execucao.Atributo("oms.resultado"))
            .Should().Be((2, 1, "concluido"));
        coletor.Soma("oms.rotinas.itens", ("oms.rotina", "reenvio_dlq"), ("oms.resultado", "concluido")).Should().Be(1, "métrica 5 (US5)");

        var reenvioComOrigem = coletor.Span(s => s.DisplayName == "reenviar pedido.criado" && Equals(s.Atributo("messaging.message.id"), comOrigem))!;
        reenvioComOrigem.TraceId.Should().Be(origem.TraceId, "a cópia continua o trace de origem");
        reenvioComOrigem.Links.Select(l => l.Context.SpanId).Should().Equal(execucao.SpanId);
        coletor.Span(s => EhEntregaDe(s, comOrigem))!.TraceId.Should().Be(origem.TraceId, "o consumo da cópia fica no trace de origem");

        var reenvioSemOrigem = coletor.Span(s => s.DisplayName == "reenviar pedido.criado" && Equals(s.Atributo("messaging.message.id"), semOrigem))!;
        reenvioSemOrigem.ParentSpanId.Should().Be(execucao.SpanId, "sem origem, a cópia fica no trace da execução");

        coletor.Spans.Should().NotContain(s => s.DisplayName.StartsWith("reenviar ", StringComparison.Ordinal) && Equals(s.Atributo("messaging.message.id"), bloqueada),
            "a mensagem acima do limite não é reenviada");
    }

    private static bool EhEntregaDe(Activity span, string idMensagem) =>
        span.Source.Name == "RabbitMQ.Client.Subscriber" && Equals(span.Atributo("oms.consumidor"), Consumidor)
        && Equals(span.Atributo("messaging.message.id"), idMensagem);

    /// <summary>Como <c>CenarioReenvio.WorkerReenviando</c> com o estoque, e com a telemetria ligada.</summary>
    private FabricaWorker Worker(string stringConexao, ColetorTelemetria coletor) => new()
    {
        StringConexao = stringConexao,
        ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
        Configuracoes = new Dictionary<string, string?> { ["ReenvioDlq:Habilitado"] = "true", ["ReenvioDlq:IntervaloSegundos"] = "1" },
        Telemetria = coletor,
        Servicos = colecao =>
        {
            CenarioEstoque.RemoverConsumidor<ProcessamentoPagamento, EstoqueReservado>(colecao);
            CenarioEstoque.RemoverConsumidor<CriacaoOrdemEntrega, PagamentoAprovado>(colecao);
        }
    };
}
