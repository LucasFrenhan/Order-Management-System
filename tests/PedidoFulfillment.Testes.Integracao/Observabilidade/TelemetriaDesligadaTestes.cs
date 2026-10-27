using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Entregas;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Observabilidade.CenarioObservabilidade;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Observabilidade;

/// <summary>
/// User Story 6 do REQ-16 (T031): desligada, nada muda; com o destino fora do ar, os fluxos
/// seguem. Na coleção sem paralelismo: com outro teste ligado ao mesmo tempo, os ouvintes
/// globais fariam a telemetria "desligada" gravar contexto (analyze U1).
/// </summary>
[Collection(nameof(ColecaoObservabilidade))]
public sealed class TelemetriaDesligadaTestes(MensageriaTeste mensageria)
{
    [Fact]
    public async Task Desligada_nao_grava_contexto_nem_traceparent()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        await EsperarSemOuvintesAsync();
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao, null);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, null);
        await using var fila = await mensageria.FilaTesteAsync(TiposEvento.PedidoCriado);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        (await CenarioEntrega.EsperarOrdemAsync(stringConexao, pedidoId, Prazo)).Should().BeTrue("o fluxo é o de antes (FR-020)");
        (await ContextosGravadosAsync(stringConexao, pedidoId)).Should().Be(0, "nenhuma linha ganha contexto");
        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(!fila.Mensagens.IsEmpty), Prazo)).Should().BeTrue();
        fila.Mensagens.Should().OnlyContain(m => m.Propriedades.Headers == null || !m.Propriedades.Headers.ContainsKey("traceparent"),
            "sem ouvinte, o RabbitMQ.Client não injeta o contexto");
    }

    [Fact]
    public async Task Destino_fora_do_ar_nao_atrapalha_os_fluxos()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        var destinoFechado = new Dictionary<string, string?> { ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:1" };
        await using var api = Api(stringConexao, null, destinoFechado);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, null, destinoFechado);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        (await CenarioEntrega.EsperarOrdemAsync(stringConexao, pedidoId, Prazo)).Should().BeTrue("a exportação é em segundo plano e descarta o que não envia (FR-021)");
        (await ContextosGravadosAsync(stringConexao, pedidoId)).Should().BeGreaterThan(0, "com destino, a telemetria está ligada");
    }

    [Fact]
    public async Task Ligada_so_na_api_o_fluxo_termina()
    {
        // No processo de teste, os ouvintes são globais: o Worker "desligado" ainda vê as fontes
        // ouvidas pela API. O que se prova aqui é que o fluxo termina com a configuração mista.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var coletor = new ColetorTelemetria();
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao, coletor);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, null);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        (await CenarioEntrega.EsperarOrdemAsync(stringConexao, pedidoId, Prazo)).Should().BeTrue();
    }

    private static Task<int> ContextosGravadosAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT (SELECT COUNT(*) FROM Pedidos WHERE Id = '{pedidoId}' AND {ContextoBancoDados.ContextoRastreio} IS NOT NULL) " +
            $"+ (SELECT COUNT(*) FROM EventosSaida WHERE Conteudo LIKE '%{pedidoId}%' AND {ContextoBancoDados.ContextoRastreio} IS NOT NULL)");
}
