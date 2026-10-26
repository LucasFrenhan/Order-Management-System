using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Testes.Integracao.Entregas;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Observabilidade.CenarioObservabilidade;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Observabilidade;

/// <summary>User Story 4 do REQ-16 (T024): os logs carregam o trace e o passo, sem mudar.</summary>
[Collection(nameof(ColecaoObservabilidade))]
public sealed class LogsCorrelacionadosTestes(MensageriaTeste mensageria)
{
    /// <summary>Os logs do caminho feliz, com nome e nível documentados em docs/fluxos/.</summary>
    private static readonly (int Id, string Nome, LogLevel Nivel)[] LogsDoFluxo =
    [
        (4001, "PedidoCriado", LogLevel.Information),
        (5001, "EventoPublicado", LogLevel.Information),
        (5006, "EventoProcessado", LogLevel.Information),
        (6001, "EstoqueReservado", LogLevel.Information),
        (7001, "PagamentoAprovado", LogLevel.Information),
        (8001, "OrdemEntregaCriada", LogLevel.Information)
    ];

    [Fact]
    public async Task Logs_da_api_e_do_worker_saem_com_o_trace_do_pedido_e_os_mesmos_ids()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var coletor = new ColetorTelemetria();
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao, coletor);
        var (cliente, email, _) = await ClienteAsync(api);
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, coletor);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await CenarioEntrega.EsperarOrdemAsync(stringConexao, pedidoId, Prazo)).Should().BeTrue();
        var traceId = coletor.Span(s => s.EhRequisicao("POST", "/api/v1/pedidos/") && Equals(s.Atributo("oms.pedido.id"), pedidoId.ToString()))!.TraceId;
        (await MensageriaTeste.EsperarAteAsync(
            () => Task.FromResult(coletor.Logs.Count(l => l.TraceId == traceId && l.Evento.Id == 8001) == 1), Prazo)).Should().BeTrue();

        var doTrace = coletor.Logs.Where(l => l.TraceId == traceId).ToList();
        var spans = coletor.DoTrace(traceId).Select(s => s.SpanId).ToHashSet();
        foreach (var (id, nome, nivel) in LogsDoFluxo)
        {
            var registros = doTrace.Where(l => l.Evento.Id == id).ToList();
            registros.Should().NotBeEmpty($"o log {id} sai no trace do pedido");
            registros.Should().OnlyContain(l => l.Evento.Name == nome && l.Nivel == nivel, $"o log {id} não muda (FR-015)");
            registros.Should().OnlyContain(l => spans.Contains(l.SpanId), $"o log {id} aponta para um passo do trace");
        }

        doTrace.Should().NotContain(l => (l.Mensagem ?? "").Contains(email, StringComparison.OrdinalIgnoreCase), "logs sem e-mail");
    }

    [Fact]
    public async Task Console_mostra_o_trace_nos_escopos_mesmo_com_a_telemetria_desligada()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        await using var worker = new FabricaWorker { StringConexao = stringConexao, ConfiguracoesBroker = mensageria.ConfiguracoesBroker };
        worker.CreateClient().Dispose();

        worker.Services.GetRequiredService<IOptions<LoggerFactoryOptions>>().Value.ActivityTrackingOptions
            .Should().HaveFlag(ActivityTrackingOptions.TraceId).And.HaveFlag(ActivityTrackingOptions.SpanId);
        worker.Services.GetRequiredService<IOptionsMonitor<SimpleConsoleFormatterOptions>>().CurrentValue.IncludeScopes
            .Should().BeTrue("o console mostra os escopos, onde ficam o trace e o passo (FR-014)");
    }
}
