using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.PagamentosHttp;

/// <summary>
/// A API de pagamento simulada chamada diretamente (REQ-19, T016; contrato da API): autenticação,
/// idempotência pela chave, consulta, regras do estorno e rota de controle. Sem banco nem broker.
/// </summary>
public sealed class ApiPagamentosSimuladaTestes : IDisposable
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private readonly FabricaPagamentos api = FabricaPagamentos.Iniciar();

    public void Dispose() => api.Dispose();

    internal static async Task<HttpResponseMessage> CobrarAsync(HttpClient cliente, Guid chave, decimal valor)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "v1/cobrancas") { Content = JsonContent.Create(new { valor }) };
        requisicao.Headers.Add("Idempotency-Key", chave.ToString());
        return await cliente.SendAsync(requisicao, Cancelamento);
    }

    internal static async Task<HttpResponseMessage> EstornarAsync(HttpClient cliente, Guid chave, string identificadorCobranca, decimal valor)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "v1/estornos") { Content = JsonContent.Create(new { identificadorCobranca, valor }) };
        requisicao.Headers.Add("Idempotency-Key", chave.ToString());
        return await cliente.SendAsync(requisicao, Cancelamento);
    }

    internal static async Task<JsonElement> CorpoAsync(HttpResponseMessage resposta) =>
        await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento);

    [Fact]
    public async Task Health_responde_sem_credencial()
    {
        using var cliente = api.ClienteHttp();

        (await cliente.GetAsync("health", Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("chave-errada-qualquer-1234")]
    [InlineData(FabricaPagamentos.ChaveControleTeste)]
    public async Task Sem_a_chave_do_OMS_a_cobranca_da_401_uniforme_e_nada_e_guardado(string? chave)
    {
        using var cliente = api.ClienteHttp();
        if (chave is not null)
        {
            cliente.DefaultRequestHeaders.Add("X-Api-Key", chave);
        }

        var pedido = Guid.CreateVersion7();
        var resposta = await CobrarAsync(cliente, pedido, 10m);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CorpoAsync(resposta)).GetProperty("title").GetString().Should().Be("Credencial inválida.");
        (await api.OperacaoAsync("cobranca", pedido)).Should().BeNull();
    }

    [Fact]
    public async Task A_rota_de_controle_recusa_a_chave_do_OMS_e_nada_muda()
    {
        using var cliente = api.ClienteApi();

        var resposta = await cliente.PutAsJsonAsync("controle/modo", new { modo = "indisponivel" }, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CobrarAsync(cliente, Guid.CreateVersion7(), 10m)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Aprova_com_identificador_PAG_e_a_repeticao_devolve_o_mesmo_com_Idempotent_Replayed()
    {
        using var cliente = api.ClienteApi();
        var pedido = Guid.CreateVersion7();

        var primeira = await CobrarAsync(cliente, pedido, 149.70m);
        var repeticao = await CobrarAsync(cliente, pedido, 149.70m);

        primeira.StatusCode.Should().Be(HttpStatusCode.Created);
        var corpo = await CorpoAsync(primeira);
        corpo.GetProperty("situacao").GetString().Should().Be("aprovada");
        corpo.GetProperty("identificador").GetString().Should().MatchRegex("^PAG-[0-9a-f]{32}$");
        corpo.GetProperty("valor").GetDecimal().Should().Be(149.70m);
        repeticao.StatusCode.Should().Be(HttpStatusCode.OK);
        repeticao.Headers.GetValues("Idempotent-Replayed").Should().Equal("true");
        (await CorpoAsync(repeticao)).GetProperty("identificador").GetString().Should().Be(corpo.GetProperty("identificador").GetString());
        (await api.OperacaoAsync("cobranca", pedido)).Should().Be(new PagamentosSimulados.Simulacao.RespostaOperacaoControle("aprovada", 1, 2));
    }

    [Fact]
    public async Task Recusa_pelo_roteiro_com_motivo_e_sem_identificador()
    {
        using var cliente = api.ClienteApi();
        var pedido = Guid.CreateVersion7();
        await api.RoteiroAsync("recusar");

        var corpo = await CorpoAsync(await CobrarAsync(cliente, pedido, 10m));

        corpo.GetProperty("situacao").GetString().Should().Be("recusada");
        corpo.GetProperty("motivo").GetString().Should().Be("Pagamento recusado pelo emissor.");
        corpo.GetProperty("identificador").ValueKind.Should().Be(JsonValueKind.Null);
        (await api.OperacaoAsync("cobranca", pedido))!.Efetivacoes.Should().Be(1);
    }

    [Fact]
    public async Task A_mesma_chave_com_outro_valor_e_chave_reutilizada_sem_cobrar()
    {
        using var cliente = api.ClienteApi();
        var pedido = Guid.CreateVersion7();
        await CobrarAsync(cliente, pedido, 10m);

        var resposta = await CobrarAsync(cliente, pedido, 11m);

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        resposta.Headers.RetryAfter.Should().BeNull("só a operação em andamento manda Retry-After");
        (await CorpoAsync(resposta)).GetProperty("codigo").GetString().Should().Be("chave-reutilizada");
        (await api.OperacaoAsync("cobranca", pedido))!.Efetivacoes.Should().Be(1);
    }

    [Theory]
    [InlineData(null, "10")]
    [InlineData("nao-e-guid", "10")]
    [InlineData("guid", "0")]
    [InlineData("guid", "-1")]
    [InlineData("guid", "10.001")]
    public async Task Chave_ou_valor_invalidos_dao_400(string? chave, string valor)
    {
        using var cliente = api.ClienteApi();
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, "v1/cobrancas")
        {
            Content = new StringContent($"{{\"valor\":{valor}}}", System.Text.Encoding.UTF8, "application/json")
        };
        if (chave is not null)
        {
            requisicao.Headers.Add("Idempotency-Key", chave == "guid" ? Guid.CreateVersion7().ToString() : chave);
        }

        var resposta = await cliente.SendAsync(requisicao, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CorpoAsync(resposta)).TryGetProperty("errors", out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_consulta_devolve_a_cobranca_ou_404_nao_encontrada()
    {
        using var cliente = api.ClienteApi();
        var pedido = Guid.CreateVersion7();

        var antes = await cliente.GetAsync($"v1/cobrancas/{pedido}", Cancelamento);
        var cobranca = await CorpoAsync(await CobrarAsync(cliente, pedido, 10m));
        var depois = await cliente.GetAsync($"v1/cobrancas/{pedido}", Cancelamento);

        antes.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CorpoAsync(antes)).GetProperty("codigo").GetString().Should().Be("nao-encontrada");
        depois.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CorpoAsync(depois)).GetProperty("identificador").GetString().Should().Be(cobranca.GetProperty("identificador").GetString());
    }

    [Fact]
    public async Task O_estorno_confirma_com_EST_e_recusa_cobranca_desconhecida_ou_valor_diferente()
    {
        using var cliente = api.ClienteApi();
        var pedido = Guid.CreateVersion7();
        var transacao = (await CorpoAsync(await CobrarAsync(cliente, pedido, 10m))).GetProperty("identificador").GetString()!;

        var confirmado = await CorpoAsync(await EstornarAsync(cliente, pedido, transacao, 10m));
        var desconhecida = await CorpoAsync(await EstornarAsync(cliente, Guid.CreateVersion7(), "PAG-desconhecido", 10m));
        var outroPedido = Guid.CreateVersion7();
        var outraTransacao = (await CorpoAsync(await CobrarAsync(cliente, outroPedido, 20m))).GetProperty("identificador").GetString()!;
        var valorDiferente = await CorpoAsync(await EstornarAsync(cliente, outroPedido, outraTransacao, 5m));

        confirmado.GetProperty("situacao").GetString().Should().Be("confirmado");
        confirmado.GetProperty("identificador").GetString().Should().MatchRegex("^EST-[0-9a-f]{32}$");
        desconhecida.GetProperty("situacao").GetString().Should().Be("recusado");
        desconhecida.GetProperty("motivo").GetString().Should().Be("Cobrança desconhecida.");
        valorDiferente.GetProperty("motivo").GetString().Should().Be("Valor diferente do cobrado.");
        (await EstornarAsync(cliente, pedido, transacao, 10m)).Headers.GetValues("Idempotent-Replayed").Should().Equal("true");
        (await api.OperacaoAsync("estorno", pedido))!.Efetivacoes.Should().Be(1);
    }

    [Fact]
    public async Task Restaurar_apaga_as_operacoes_e_volta_a_configuracao()
    {
        using var cliente = api.ClienteApi();
        var pedido = Guid.CreateVersion7();
        await CobrarAsync(cliente, pedido, 10m);
        await api.ModoAsync("indisponivel");

        await api.RestaurarAsync();

        (await api.OperacaoAsync("cobranca", pedido)).Should().BeNull();
        (await CobrarAsync(cliente, Guid.CreateVersion7(), 10m)).StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
