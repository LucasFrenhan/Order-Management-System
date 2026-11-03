using System.Net;
using FluentAssertions;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Erros;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Endpoints;

/// <summary>REQ-21: GET /api/v1/pedidos exige token e um dos três perfis (FR-002, FR-003).</summary>
public sealed class ListarPedidosTestes(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    private const string Rota = "/api/v1/pedidos";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sem_token_responde_401()
    {
        var resposta = await fabrica.CreateClient().GetAsync(Rota, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await FormatoErroTestes.DeveSerProblemDetails(resposta, 401, Rota);
    }

    [Fact]
    public async Task Token_com_assinatura_invalida_responde_401()
    {
        var token = GeradorTokenTeste.Gerar(perfis: [PerfilUsuario.Admin], assinaturaInvalida: true);

        var resposta = await fabrica.CreateClient().ComToken(token).GetAsync(Rota, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_sem_nenhum_perfil_responde_403_antes_da_validacao()
    {
        var resposta = await fabrica.CreateClient().ComToken().GetAsync($"{Rota}?tamanho=0", Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await FormatoErroTestes.DeveSerProblemDetails(resposta, 403, Rota);
    }

    [Fact]
    public async Task Parametro_invalido_responde_400_em_validacao_problem_details()
    {
        var resposta = await fabrica.CreateClient().ComToken(PerfilUsuario.Cliente).GetAsync($"{Rota}?tamanho=0&status=Enviado", Cancelamento);

        (await EndpointsAutenticacaoTestes.ErrosDe(resposta, Rota)).Keys.Should().BeEquivalentTo(["tamanho", "status"]);
    }
}
