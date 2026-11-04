using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Contrato;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.LimiteRequisicoes;

/// <summary>O 429 padronizado e documentado (REQ-23, US3, FR-013, FR-016, SC-006).</summary>
public sealed class RespostaLimiteTestes(BancoDadosTeste banco)
{
    private const string EmailComConta = "maria@exemplo.com.br";
    private const string EmailSemConta = "ninguem@exemplo.com.br";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(CenarioLimite.RotaLogin)]
    [InlineData(CenarioLimite.RotaRegistro)]
    public async Task O_429_traz_o_problema_o_codigo_e_o_retry_after(string rota)
    {
        var limites = CenarioLimite.Limites(loginOrigem: 1, registro: 1);
        limites["LimiteRequisicoes:LoginOrigem:JanelaSegundos"] = "60";
        limites["LimiteRequisicoes:Registro:JanelaSegundos"] = "60";
        await using var api = CenarioLimite.SemBanco(limites);
        var cliente = api.CreateClient();
        // A primeira requisição é recusada pela validação (corpo sem campos), mas conta.
        await cliente.PostAsJsonAsync(rota, new { }, Cancelamento);

        var resposta = await cliente.PostAsJsonAsync(rota, new { }, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        CenarioLimite.RetryAfter(resposta).Should().BeInRange(1, 60);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento);
        corpo.GetProperty("status").GetInt32().Should().Be(429);
        corpo.GetProperty("title").GetString().Should().Be("Muitas requisições.");
        corpo.GetProperty("detail").GetString().Should().Be("Aguarde o tempo indicado no cabeçalho Retry-After e tente de novo.");
        corpo.GetProperty("instance").GetString().Should().Be(rota);
        corpo.GetProperty("traceId").GetString().Should().NotBeNullOrEmpty();
        corpo.GetProperty("codigo").GetString().Should().Be("LIMITE_REQUISICOES_EXCEDIDO");
    }

    [Fact]
    public async Task O_corpo_e_igual_para_conta_existente_inexistente_e_bloqueada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = CenarioLimite.Instancia(banco.NovoBanco(), CenarioLimite.Limites(loginOrigemEmail: 6));
        var cliente = api.CreateClient();
        await CenarioLimite.RegistroAsync(cliente, EmailComConta);
        await CenarioLimite.RegistroAsync(cliente, "bloqueada@exemplo.com.br");
        var comConta = await EsgotarAsync(cliente, EmailComConta, CenarioLimite.SenhaValida);
        var semConta = await EsgotarAsync(cliente, EmailSemConta, CenarioLimite.SenhaValida);
        var bloqueada = await EsgotarAsync(cliente, "bloqueada@exemplo.com.br", CenarioLimite.SenhaErrada);

        comConta.Should().Be(semConta).And.Be(bloqueada);
    }

    [Theory]
    [InlineData(CenarioLimite.RotaLogin)]
    [InlineData(CenarioLimite.RotaRegistro)]
    public async Task O_documento_gerado_declara_o_429_com_retry_after(string rota)
    {
        await using var api = new FabricaApi();
        var gerado = await LeitorContrato.CarregarGeradoAsync(api.CreateClient());

        var resposta = gerado.Paths[rota].Operations![HttpMethod.Post].Responses!["429"];

        resposta.Headers!.Should().ContainKey("Retry-After");
    }

    [Theory]
    [InlineData(CenarioLimite.RotaLogin)]
    [InlineData(CenarioLimite.RotaRegistro)]
    public async Task O_contrato_aprovado_declara_o_429_com_retry_after(string rota)
    {
        var contrato = await LeitorContrato.CarregarContratoAsync();

        var resposta = contrato.Paths[rota].Operations![HttpMethod.Post].Responses!["429"];

        resposta.Headers!.Should().ContainKey("Retry-After");
    }

    /// <summary>O corpo do 429, sem o <c>traceId</c>, depois de esgotar as 6 tentativas do e-mail.</summary>
    private static async Task<string> EsgotarAsync(HttpClient cliente, string email, string senha)
    {
        for (var i = 0; i < 6; i++)
        {
            await CenarioLimite.LoginAsync(cliente, email, senha);
        }

        var resposta = await CenarioLimite.LoginAsync(cliente, email, senha);
        resposta.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento);
        return string.Join("|", corpo.EnumerateObject().Where(p => p.Name != "traceId").Select(p => $"{p.Name}={p.Value}"));
    }
}
