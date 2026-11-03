using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Erros;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Endpoints;

/// <summary>
/// REQ-22: as 7 rotas do catálogo exigem token; a leitura é dos três perfis e a gestão, só do
/// Admin (FR-002, FR-008). A autorização vem antes da validação e do serviço.
/// </summary>
public sealed class ProdutosTestes(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    private static readonly string Id = Guid.NewGuid().ToString();

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Rotas => new()
    {
        { "GET", "/api/v1/produtos" },
        { "GET", $"/api/v1/produtos/{Id}" },
        { "POST", "/api/v1/produtos" },
        { "PUT", $"/api/v1/produtos/{Id}" },
        { "POST", $"/api/v1/produtos/{Id}/ajustes-estoque" },
        { "POST", $"/api/v1/produtos/{Id}/desativacao" },
        { "POST", $"/api/v1/produtos/{Id}/ativacao" }
    };

    public static TheoryData<string, string> RotasDeGestao => new()
    {
        { "POST", "/api/v1/produtos" },
        { "PUT", $"/api/v1/produtos/{Id}" },
        { "POST", $"/api/v1/produtos/{Id}/ajustes-estoque" },
        { "POST", $"/api/v1/produtos/{Id}/desativacao" },
        { "POST", $"/api/v1/produtos/{Id}/ativacao" }
    };

    private static HttpRequestMessage Requisicao(string metodo, string rota) => new(new HttpMethod(metodo), rota)
    {
        Content = metodo == "GET" ? null : JsonContent.Create(new { })
    };

    [Theory]
    [MemberData(nameof(Rotas))]
    public async Task Sem_token_ou_com_assinatura_invalida_responde_401(string metodo, string rota)
    {
        var semToken = await fabrica.CreateClient().SendAsync(Requisicao(metodo, rota), Cancelamento);
        var invalido = await fabrica.CreateClient()
            .ComToken(GeradorTokenTeste.Gerar(perfis: [PerfilUsuario.Admin], assinaturaInvalida: true))
            .SendAsync(Requisicao(metodo, rota), Cancelamento);

        semToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await FormatoErroTestes.DeveSerProblemDetails(semToken, 401, rota);
        invalido.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Rotas))]
    public async Task Token_sem_nenhum_perfil_responde_403(string metodo, string rota)
    {
        var resposta = await fabrica.CreateClient().ComToken().SendAsync(Requisicao(metodo, rota), Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(RotasDeGestao))]
    public async Task Cliente_e_operador_logistica_nao_gerenciam_o_catalogo(string metodo, string rota)
    {
        foreach (var perfil in new[] { PerfilUsuario.Cliente, PerfilUsuario.OperadorLogistica })
        {
            var resposta = await fabrica.CreateClient().ComToken(perfil).SendAsync(Requisicao(metodo, rota), Cancelamento);

            resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, perfil.ToString());
            await FormatoErroTestes.DeveSerProblemDetails(resposta, 403, rota);
        }
    }

    [Fact]
    public async Task Identificador_que_nao_e_uuid_responde_400()
    {
        var resposta = await fabrica.CreateClient().ComToken(PerfilUsuario.Cliente).GetAsync("/api/v1/produtos/abc", Cancelamento);

        (await EndpointsAutenticacaoTestes.ErrosDe(resposta, "/api/v1/produtos/abc")).Should().ContainKey("id");
    }

    [Fact]
    public async Task Parametros_invalidos_da_listagem_voltam_todos_juntos()
    {
        var resposta = await fabrica.CreateClient().ComToken(PerfilUsuario.Admin)
            .GetAsync("/api/v1/produtos?tamanho=0&situacao=Esgotados&busca=!!", Cancelamento);

        (await EndpointsAutenticacaoTestes.ErrosDe(resposta, "/api/v1/produtos")).Keys.Should().BeEquivalentTo(["tamanho", "situacao", "busca"]);
    }
}
