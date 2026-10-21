using System.Net;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Erros;

/// <summary>
/// Formato padrão de erro (FR-009) e autenticação por padrão em qualquer endereço (FR-007a).
/// </summary>
public sealed class FormatoErroTestes(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    [Fact]
    public async Task Rota_inexistente_sem_token_responde_401_em_problem_details()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/nao-existe", TestContext.Current.CancellationToken);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        resposta.Headers.WwwAuthenticate.Should().ContainSingle(h => h.Scheme == "Bearer");
        await DeveSerProblemDetails(resposta, 401, "/nao-existe");
    }

    [Fact]
    public async Task Rota_inexistente_com_token_valido_responde_404_em_problem_details()
    {
        var cliente = fabrica.CreateClient().ComToken(PerfilUsuario.Cliente);

        var resposta = await cliente.GetAsync("/nao-existe", TestContext.Current.CancellationToken);

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await DeveSerProblemDetails(resposta, 404, "/nao-existe");
    }

    [Fact]
    public async Task Excecao_nao_tratada_responde_500_sem_detalhes_internos()
    {
        var cliente = fabrica.CreateClient().ComToken(PerfilUsuario.Admin);

        var resposta = await cliente.GetAsync(FabricaApi.RotaExcecaoTeste, TestContext.Current.CancellationToken);

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var corpo = await DeveSerProblemDetails(resposta, 500, FabricaApi.RotaExcecaoTeste);
        corpo.TryGetProperty("detail", out _).Should().BeFalse();
        var texto = corpo.GetRawText();
        texto.Should().NotContain("InvalidOperationException").And.NotContain("dado sensível").And.NotContain(" at ");
    }

    internal static async Task<JsonElement> DeveSerProblemDetails(HttpResponseMessage resposta, int status, string instancia)
    {
        resposta.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var corpo = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync()).RootElement;
        corpo.GetProperty("type").GetString().Should().NotBeNullOrWhiteSpace();
        corpo.GetProperty("title").GetString().Should().NotBeNullOrWhiteSpace();
        corpo.GetProperty("status").GetInt32().Should().Be(status);
        corpo.GetProperty("instance").GetString().Should().Be(instancia);
        corpo.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        return corpo;
    }
}
