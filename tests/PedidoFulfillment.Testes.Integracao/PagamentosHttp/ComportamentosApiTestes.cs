using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.PagamentosHttp;

/// <summary>
/// Os comportamentos de falha da API simulada, forçados pela rota de controle (REQ-19, US2, T024;
/// contrato da API). Sem banco nem broker.
/// </summary>
public sealed class ComportamentosApiTestes : IDisposable
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private readonly FabricaPagamentos api = FabricaPagamentos.Iniciar(new Dictionary<string, string?> { ["Simulacao:SemRespostaMs"] = "600" });

    public void Dispose() => api.Dispose();

    [Theory]
    [InlineData("indisponivel", HttpStatusCode.ServiceUnavailable)]
    [InlineData("limite-taxa", HttpStatusCode.TooManyRequests)]
    [InlineData("erro-interno", HttpStatusCode.InternalServerError)]
    public async Task Falhas_respondem_o_codigo_do_contrato_e_deixam_a_chave_livre(string comportamento, HttpStatusCode status)
    {
        using var cliente = api.ClienteApi();
        var pedido = Guid.CreateVersion7();
        await api.RoteiroAsync(comportamento);

        var falha = await ApiPagamentosSimuladaTestes.CobrarAsync(cliente, pedido, 10m);
        var depois = await ApiPagamentosSimuladaTestes.CobrarAsync(cliente, pedido, 10m);

        falha.StatusCode.Should().Be(status);
        (await ApiPagamentosSimuladaTestes.CorpoAsync(falha)).GetProperty("codigo").GetString().Should().Be(comportamento);
        if (status == HttpStatusCode.TooManyRequests)
        {
            falha.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(1));
        }

        depois.StatusCode.Should().Be(HttpStatusCode.Created, "a falha não processou: a chave ficou livre");
        (await api.OperacaoAsync("cobranca", pedido))!.Efetivacoes.Should().Be(1);
    }

    [Fact]
    public async Task Sem_resposta_segura_e_depois_504_sem_processar()
    {
        using var cliente = api.ClienteApi();
        var pedido = Guid.CreateVersion7();
        await api.RoteiroAsync("sem-resposta");
        var cronometro = Stopwatch.StartNew();

        var resposta = await ApiPagamentosSimuladaTestes.CobrarAsync(cliente, pedido, 10m);

        cronometro.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(550));
        resposta.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
        (await api.OperacaoAsync("cobranca", pedido)).Should().BeNull();
    }

    [Fact]
    public async Task Processar_sem_responder_efetiva_antes_de_responder_e_a_repeticao_devolve_o_resultado()
    {
        using var cliente = api.ClienteApi();
        cliente.Timeout = TimeSpan.FromMilliseconds(200);
        var pedido = Guid.CreateVersion7();
        await api.RoteiroAsync("processar-sem-responder");

        var abandonada = () => ApiPagamentosSimuladaTestes.CobrarAsync(cliente, pedido, 10m);
        await abandonada.Should().ThrowAsync<TaskCanceledException>("o cliente desiste antes da resposta");

        using var outro = api.ClienteApi();
        var repeticao = await ApiPagamentosSimuladaTestes.CobrarAsync(outro, pedido, 10m);
        repeticao.StatusCode.Should().Be(HttpStatusCode.OK);
        repeticao.Headers.GetValues("Idempotent-Replayed").Should().Equal("true");
        (await api.OperacaoAsync("cobranca", pedido)).Should().Be(new PagamentosSimulados.Simulacao.RespostaOperacaoControle("aprovada", 1, 2));
    }

    [Fact]
    public async Task Falha_forcada_sobre_uma_chave_concluida_responde_a_falha_e_nao_apaga_a_operacao()
    {
        // Research R-03: a decisão vem antes da memória, e a operação concluída nunca é removida.
        using var cliente = api.ClienteApi();
        var pedido = Guid.CreateVersion7();
        await ApiPagamentosSimuladaTestes.CobrarAsync(cliente, pedido, 10m);
        await api.RoteiroAsync("indisponivel", "sem-resposta");

        (await ApiPagamentosSimuladaTestes.CobrarAsync(cliente, pedido, 10m)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ApiPagamentosSimuladaTestes.CobrarAsync(cliente, pedido, 10m)).StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);

        (await api.OperacaoAsync("cobranca", pedido)).Should().Be(new PagamentosSimulados.Simulacao.RespostaOperacaoControle("aprovada", 1, 3));
    }

    [Fact]
    public async Task A_consulta_ignora_as_falhas_sorteadas_mas_nao_o_modo_forcado()
    {
        using var cliente = api.ClienteApi();
        var pedido = Guid.CreateVersion7();
        await ApiPagamentosSimuladaTestes.CobrarAsync(cliente, pedido, 10m);
        await api.AlterarComportamentoAsync(new { indisponivelPercentual = 100, taxaAprovacaoPercentual = 0 });

        (await cliente.GetAsync($"v1/cobrancas/{pedido}", Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK, "a consulta não sofre falha sorteada");
        await api.ModoAsync("indisponivel");
        (await cliente.GetAsync($"v1/cobrancas/{pedido}", Cancelamento)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task A_latencia_definida_pelo_controle_vale_no_modo_deterministico_e_restaurar_a_desfaz()
    {
        using var cliente = api.ClienteApi();
        await api.AlterarComportamentoAsync(new { latenciaMinimaMs = 250, latenciaMaximaMs = 250 });

        var lenta = Stopwatch.StartNew();
        await ApiPagamentosSimuladaTestes.CobrarAsync(cliente, Guid.CreateVersion7(), 10m);
        lenta.Stop();
        await api.RestaurarAsync();
        var rapida = Stopwatch.StartNew();
        await ApiPagamentosSimuladaTestes.CobrarAsync(cliente, Guid.CreateVersion7(), 10m);
        rapida.Stop();

        lenta.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(240));
        rapida.Elapsed.Should().BeLessThan(lenta.Elapsed);
    }

    [Fact]
    public async Task Modo_forcado_vale_ate_ser_desfeito()
    {
        using var cliente = api.ClienteApi();
        await api.ModoAsync("erro-interno");

        (await ApiPagamentosSimuladaTestes.CobrarAsync(cliente, Guid.CreateVersion7(), 10m)).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await ApiPagamentosSimuladaTestes.CobrarAsync(cliente, Guid.CreateVersion7(), 10m)).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        await api.DesfazerModoAsync();
        (await ApiPagamentosSimuladaTestes.CobrarAsync(cliente, Guid.CreateVersion7(), 10m)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("{\"indisponivelPercentual\":101}")]
    [InlineData("{\"latenciaMinimaMs\":500,\"latenciaMaximaMs\":100}")]
    [InlineData("{\"indisponivelPercentual\":60,\"erroInternoPercentual\":60}")]
    public async Task Comportamento_invalido_da_400_e_nada_muda(string corpo)
    {
        using var controle = api.ClienteHttp();
        controle.DefaultRequestHeaders.Add("X-Chave-Controle", FabricaPagamentos.ChaveControleTeste);

        var resposta = await controle.PutAsync("controle/comportamento", new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"), Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var cliente = api.ClienteApi();
        (await ApiPagamentosSimuladaTestes.CobrarAsync(cliente, Guid.CreateVersion7(), 10m)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Modo_ou_roteiro_desconhecido_da_400()
    {
        using var controle = api.ClienteHttp();
        controle.DefaultRequestHeaders.Add("X-Chave-Controle", FabricaPagamentos.ChaveControleTeste);

        (await controle.PutAsJsonAsync("controle/modo", new { modo = "fora-do-ar" }, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await controle.PostAsJsonAsync("controle/roteiro", new { comportamentos = new[] { "aprovar", "x" } }, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await controle.GetAsync("controle/comportamento", Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
