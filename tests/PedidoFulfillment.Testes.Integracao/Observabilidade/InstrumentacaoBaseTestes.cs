using System.Diagnostics;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Observabilidade.CenarioObservabilidade;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Observabilidade;

/// <summary>
/// Base da telemetria (REQ-16, T009): requisição com consultas filhas sem valores, rotas de
/// saúde fora e o polling do despachante sem traces (R-09). Valida o risco do plano: a
/// instrumentação do SqlClient 1.19 com o Microsoft.Data.SqlClient 7.1.1.
/// </summary>
[Collection(nameof(ColecaoObservabilidade))]
public sealed class InstrumentacaoBaseTestes(MensageriaTeste mensageria)
{
    [Fact]
    public async Task Requisicao_gera_span_com_consultas_filhas_sem_valores_dos_parametros()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var coletor = new ColetorTelemetria();
        await using var api = Api(mensageria.NovoBanco(), coletor);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        (await cliente.GetAsync($"/api/v1/pedidos/{pedidoId}", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

        var requisicao = await coletor.EsperarSpanAsync(s => s.EhRequisicao("GET", "/api/v1/pedidos/{id}"), Prazo);
        requisicao.Should().NotBeNull("a requisição gera um span com a rota");
        var consultas = coletor.DoTrace(requisicao!.TraceId).Where(s => s.EhConsultaBanco()).ToList();
        consultas.Should().NotBeEmpty("as consultas da requisição aparecem como filhas (FR-012)");
        consultas.Should().OnlyContain(s => s.ParentSpanId != default);
        consultas.Select(s => s.Atributo("db.query.text") as string).Should().Contain(texto => texto != null && texto.Contains("Pedidos"),
            "o texto parametrizado da consulta é gravado (clarify 4)");
        coletor.Spans.SelectMany(s => s.TagObjects)
            .Where(t => t.Value?.ToString()?.Contains(pedidoId.ToString(), StringComparison.OrdinalIgnoreCase) == true)
            .Select(t => t.Key)
            .Should().OnlyContain(chave => chave == "oms.pedido.id" || chave == "url.path",
                "o id só aparece como atributo do pedido ou no caminho, nunca como valor de parâmetro de SQL");
    }

    [Fact]
    public async Task Rotas_de_saude_nao_geram_span()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var coletor = new ColetorTelemetria();
        await using var api = Api(mensageria.NovoBanco(), coletor);
        var http = api.CreateClient();

        await http.GetAsync("/health/pronto", TestContext.Current.CancellationToken);
        await http.GetAsync("/health", TestContext.Current.CancellationToken);

        (await MensageriaTeste.EsperarAteAsync(
                () => Task.FromResult(coletor.Spans.Any(s => s.Kind == ActivityKind.Server)), TimeSpan.FromSeconds(1)))
            .Should().BeFalse("as rotas de saúde ficam fora (FR-001)");
    }

    [Fact]
    public async Task Despachante_ocioso_nao_gera_trace()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var coletor = new ColetorTelemetria();
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, coletor);

        // O despacho roda a cada 100 ms nos testes: 1 s cobre uns dez ciclos vazios.
        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(!coletor.Spans.IsEmpty()), TimeSpan.FromSeconds(1)))
            .Should().BeFalse("ciclos sem evento e consultas sem pai não geram trace (FR-011, R-09)");
    }
}

file static class Extensoes
{
    public static bool IsEmpty<T>(this IReadOnlyCollection<T> colecao) => colecao.Count == 0;
}
