using System.Diagnostics;
using FluentAssertions;
using PedidoFulfillment.Infrastructure.Observabilidade;
using PedidoFulfillment.Testes.Integracao.Autenticacao;
using PedidoFulfillment.Testes.Integracao.Entregas;
using PedidoFulfillment.Testes.Integracao.Estoque;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Pagamentos;

namespace PedidoFulfillment.Testes.Integracao.Observabilidade;

/// <summary>
/// Testes de telemetria (REQ-16) rodam sozinhos (analyze U1): os ouvintes de
/// <see cref="ActivitySource"/> valem para o processo inteiro, e um teste ligado em paralelo
/// faria um teste desligado ver spans, contexto gravado e <c>traceparent</c>.
/// </summary>
[CollectionDefinition(nameof(ColecaoObservabilidade), DisableParallelization = true)]
public sealed class ColecaoObservabilidade : ICollectionFixture<MensageriaTeste>;

/// <summary>Atalhos dos testes de telemetria: API e Worker ligados a um coletor.</summary>
internal static class CenarioObservabilidade
{
    public static readonly TimeSpan Prazo = TimeSpan.FromSeconds(20);

    /// <summary>A API com banco preparado (como <c>CenarioPedidos.Api</c>) e a telemetria ligada.</summary>
    public static FabricaApi Api(string stringConexao, ColetorTelemetria? coletor, IReadOnlyDictionary<string, string?>? extras = null) => new()
    {
        PrepararBanco = true,
        StringConexao = stringConexao,
        Configuracoes = new Dictionary<string, string?>(UsuariosIniciaisComBancoTestes.Configuracao().Concat(extras ?? new Dictionary<string, string?>())),
        Telemetria = coletor
    };

    /// <summary>
    /// O Worker completo com a telemetria ligada, depois de limpar as filas dos consumidores, e
    /// com os três consumidores prontos (como <c>CenarioEntrega.WorkerProntoAsync</c>).
    /// </summary>
    public static async Task<FabricaWorker> WorkerProntoAsync(
        MensageriaTeste mensageria, string stringConexao, ColetorTelemetria? coletor, IReadOnlyDictionary<string, string?>? extras = null,
        Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? servicos = null)
    {
        await mensageria.LimparFilasConsumidorAsync(CenarioEstoque.FilaEstoque);
        await mensageria.LimparFilasConsumidorAsync(CenarioPagamento.FilaPagamento);
        await mensageria.LimparFilasConsumidorAsync(CenarioEntrega.FilaEntrega);
        var worker = new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
            Configuracoes = extras ?? new Dictionary<string, string?>(),
            Telemetria = coletor,
            Servicos = servicos
        };
        worker.CreateClient().Dispose();
        foreach (var fila in new[] { CenarioEstoque.FilaEstoque, CenarioPagamento.FilaPagamento, CenarioEntrega.FilaEntrega })
        {
            (await mensageria.ConsumidorLigadoAsync(fila, TimeSpan.FromSeconds(10))).Should().BeTrue($"o consumidor de {fila} precisa estar pronto");
        }

        return worker;
    }

    /// <summary>
    /// O span da criação do pedido. Espera por ele: o ASP.NET Core encerra o span depois de mandar
    /// a resposta, e o cliente pode receber o 201 antes.
    /// </summary>
    public static async Task<Activity> CriacaoAsync(ColetorTelemetria coletor, Guid pedidoId)
    {
        var span = await coletor.EsperarSpanAsync(
            s => s.EhRequisicao("POST", "/api/v1/pedidos/") && Equals(s.Atributo("oms.pedido.id"), pedidoId.ToString()), Prazo);
        span.Should().NotBeNull("a criação gera o span da rota com o pedido");
        return span!;
    }

    /// <summary>
    /// Espera os provedores de testes anteriores soltarem as fontes. A fábrica da API (top-level
    /// <c>Program</c>) descarta o host numa thread própria e volta antes de o provedor de traces
    /// ser descartado: sem esperar, um teste "desligado" ainda veria o ouvinte do anterior.
    /// </summary>
    public static async Task EsperarSemOuvintesAsync() =>
        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(!TelemetriaOms.Fonte.HasListeners()), Prazo))
            .Should().BeTrue("nenhum provedor de telemetria pode continuar ativo");

    public static object? Atributo(this Activity span, string chave) => span.GetTagItem(chave);

    public static bool EhRequisicao(this Activity span, string metodo, string rota) =>
        span.Kind == ActivityKind.Server
        && Equals(span.GetTagItem("http.request.method"), metodo)
        && Equals(span.GetTagItem("http.route"), rota);

    public static bool EhConsultaBanco(this Activity span) => span.Kind == ActivityKind.Client && span.GetTagItem("db.system.name") is not null;
}
