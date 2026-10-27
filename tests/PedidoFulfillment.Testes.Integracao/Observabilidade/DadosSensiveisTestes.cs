using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Integracao.Entregas;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Observabilidade.CenarioObservabilidade;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Observabilidade;

/// <summary>
/// SC-006 e FR-023 do REQ-16 (T032): nada sensível em spans, logs exportados ou métricas, num
/// percurso com cadastro, login, criação, consulta, cancelamento e ida para a DLQ.
/// </summary>
[Collection(nameof(ColecaoObservabilidade))]
public sealed partial class DadosSensiveisTestes(MensageriaTeste mensageria)
{
    // A senha usada por CenarioPedidos.ClienteAsync.
    private const string Senha = "SenhaForte1";

    [Fact]
    public async Task Telemetria_exportada_nao_contem_email_senha_token_nem_valores_de_sql()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var coletor = new ColetorTelemetria();
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao, coletor);
        var (cliente, email, token) = await ClienteAsync(api);
        await mensageria.LimparFilasConsumidorAsync(ManipuladorFalhaControlada.Nome);
        var controle = new ControleFalhaControlada { FalharVezes = int.MaxValue };
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, coletor, servicos: servicos =>
        {
            servicos.AddSingleton(controle);
            servicos.AdicionarConsumidor<ManipuladorFalhaControlada, PedidoCriado>();
        });

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await cliente.GetAsync($"/api/v1/pedidos/{pedidoId}", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();
        (await CenarioEntrega.EsperarOrdemAsync(stringConexao, pedidoId, Prazo)).Should().BeTrue();
        var cancelado = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await MudarStatusAsync(await OperadorAsync(api), cancelado, "Cancelado", "Cliente desistiu.")).EnsureSuccessStatusCode();
        (await coletor.EsperarSpanAsync(s => Equals(s.Atributo("oms.resultado"), "dlq"), Prazo)).Should().NotBeNull("uma mensagem foi para a DLQ");

        var textos = coletor.Spans.SelectMany(s => s.TagObjects.Select(t => $"{t.Key}={t.Value}").Append(s.DisplayName))
            .Concat(coletor.Spans.SelectMany(s => s.Events.Select(e => e.Name)))
            .Concat(coletor.Logs.SelectMany(l => l.Atributos.Select(a => $"{a.Key}={a.Value}").Append(l.Mensagem ?? "")))
            .Concat(coletor.Medicoes.SelectMany(m => m.Atributos.Select(a => $"{a.Key}={a.Value}")))
            .ToList();
        textos.Should().NotBeEmpty();
        textos.Should().NotContain(t => t.Contains(email, StringComparison.OrdinalIgnoreCase), "sem e-mail");
        textos.Should().NotContain(t => t.Contains(Senha, StringComparison.Ordinal), "sem senha");
        textos.Should().NotContain(t => t.Contains(token, StringComparison.Ordinal) || t.Contains("Bearer ", StringComparison.Ordinal), "sem token");
        textos.Should().NotContain(t => t.StartsWith("http.request.header.", StringComparison.Ordinal), "sem cabeçalhos");
        coletor.Spans.Where(s => s.EhConsultaBanco()).Select(s => s.Atributo("db.query.text") as string ?? "")
            .Should().NotContain(texto => PadraoGuid().IsMatch(texto), "o texto das consultas é parametrizado, sem valores");
    }

    [GeneratedRegex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex PadraoGuid();
}
