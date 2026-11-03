using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.PagamentosHttp;

/// <summary>
/// Entradas inválidas, operação em andamento e a rota de controle da API de pagamento simulada
/// (REQ-19; contrato da API). Sem banco nem broker.
/// </summary>
public sealed class EntradasApiSimuladaTestes : IDisposable
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private readonly FabricaPagamentos api = FabricaPagamentos.Iniciar();

    public void Dispose() => api.Dispose();

    private HttpClient Controle()
    {
        var cliente = api.ClienteHttp();
        cliente.DefaultRequestHeaders.Add("X-Chave-Controle", FabricaPagamentos.ChaveControleTeste);
        return cliente;
    }

    private static async Task<HttpResponseMessage> PostarAsync(HttpClient cliente, string rota, object? corpo, Guid? chave = null)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, rota) { Content = JsonContent.Create(corpo) };
        requisicao.Headers.Add("Idempotency-Key", (chave ?? Guid.CreateVersion7()).ToString());
        return await cliente.SendAsync(requisicao, Cancelamento);
    }

    [Fact]
    public async Task Cobranca_e_estorno_sem_corpo_respondem_400_com_os_campos()
    {
        using var cliente = api.ClienteApi();

        var cobranca = await PostarAsync(cliente, "v1/cobrancas", null);
        var estorno = await PostarAsync(cliente, "v1/estornos", null);

        cobranca.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiPagamentosSimuladaTestes.CorpoAsync(cobranca)).GetProperty("errors").TryGetProperty("valor", out _).Should().BeTrue();
        estorno.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var erros = (await ApiPagamentosSimuladaTestes.CorpoAsync(estorno)).GetProperty("errors");
        erros.TryGetProperty("valor", out _).Should().BeTrue();
        erros.GetProperty("identificadorCobranca")[0].GetString().Should().Be("O identificador da cobrança é obrigatório.");
    }

    [Fact]
    public async Task Estorno_sem_identificador_da_cobranca_responde_400()
    {
        using var cliente = api.ClienteApi();

        var resposta = await PostarAsync(cliente, "v1/estornos", new { identificadorCobranca = "  ", valor = 10m });

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiPagamentosSimuladaTestes.CorpoAsync(resposta)).GetProperty("errors").TryGetProperty("valor", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Consulta_com_chave_que_nao_e_guid_responde_400()
    {
        using var cliente = api.ClienteApi();

        var resposta = await cliente.GetAsync("v1/cobrancas/nao-e-guid", Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiPagamentosSimuladaTestes.CorpoAsync(resposta)).GetProperty("errors").GetProperty("chave")[0].GetString()
            .Should().Be("A chave deve ser um GUID.");
    }

    [Fact]
    public async Task Repeticao_e_consulta_durante_o_processamento_respondem_409_em_andamento()
    {
        using var cliente = api.ClienteApi();
        await api.AlterarComportamentoAsync(new { latenciaMinimaMs = 800, latenciaMaximaMs = 800, caudaPercentual = 0 });
        var chave = Guid.CreateVersion7();

        var primeira = ApiPagamentosSimuladaTestes.CobrarAsync(cliente, chave, 10m);
        RespostaOperacao? durante = null;
        for (var i = 0; i < 50 && durante is null; i++)
        {
            await Task.Delay(20, Cancelamento);
            if (await api.OperacaoAsync("cobranca", chave) is { } operacao)
            {
                durante = new RespostaOperacao(operacao.Situacao);
            }
        }

        // A repetição e a consulta também sofrem a latência: sem ela, chegam antes de a primeira concluir.
        await api.AlterarComportamentoAsync(new { latenciaMinimaMs = 0, latenciaMaximaMs = 0 });
        var repeticao = await ApiPagamentosSimuladaTestes.CobrarAsync(cliente, chave, 10m);
        var consulta = await cliente.GetAsync($"v1/cobrancas/{chave}", Cancelamento);

        durante!.Situacao.Should().Be("em-andamento");
        (await primeira).StatusCode.Should().Be(HttpStatusCode.Created);
        foreach (var resposta in new[] { repeticao, consulta })
        {
            resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
            resposta.Headers.RetryAfter!.Delta.Should().Be(TimeSpan.FromSeconds(1));
            (await ApiPagamentosSimuladaTestes.CorpoAsync(resposta)).GetProperty("codigo").GetString().Should().Be("operacao-em-andamento");
        }
    }

    [Fact]
    public async Task Controle_altera_todos_os_parametros_informados()
    {
        using var controle = Controle();
        var parametros = new
        {
            latenciaMinimaMs = 1,
            latenciaMaximaMs = 2,
            caudaPercentual = 3.0,
            caudaMinimoMs = 4,
            caudaMaximoMs = 5,
            taxaAprovacaoPercentual = 60.0,
            indisponivelPercentual = 1.0,
            limiteTaxaPercentual = 2.0,
            retryAfterSegundos = 3,
            erroInternoPercentual = 4.0,
            semRespostaPercentual = 5.0,
            processarSemResponderPercentual = 6.0,
            semRespostaMs = 700
        };

        var resposta = await controle.PutAsJsonAsync("controle/comportamento", parametros, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var emVigor = (await ApiPagamentosSimuladaTestes.CorpoAsync(resposta)).GetProperty("parametros");
        emVigor.GetProperty("caudaMaximoMs").GetInt32().Should().Be(5);
        emVigor.GetProperty("limiteTaxaPercentual").GetDouble().Should().Be(2.0);
        emVigor.GetProperty("processarSemResponderPercentual").GetDouble().Should().Be(6.0);
        emVigor.GetProperty("semRespostaMs").GetInt32().Should().Be(700);
    }

    [Theory]
    [InlineData("{\"comportamentos\":[]}")]
    [InlineData("{}")]
    public async Task Roteiro_vazio_responde_400(string corpo)
    {
        using var controle = Controle();

        var resposta = await controle.PostAsync("controle/roteiro", new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"), Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiPagamentosSimuladaTestes.CorpoAsync(resposta)).GetProperty("errors").TryGetProperty("comportamentos", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Operacao_de_tipo_desconhecido_responde_400()
    {
        using var controle = Controle();

        var resposta = await controle.GetAsync($"controle/operacoes/assinatura/{Guid.CreateVersion7()}", Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ApiPagamentosSimuladaTestes.CorpoAsync(resposta)).GetProperty("errors").TryGetProperty("tipo", out _).Should().BeTrue();
    }

    private sealed record RespostaOperacao(string Situacao);
}
