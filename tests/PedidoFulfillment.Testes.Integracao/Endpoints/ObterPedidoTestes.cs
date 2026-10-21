using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Erros;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Endpoints;

/// <summary>
/// User Story 4: GET /api/v1/pedidos/{id} para Cliente (dono), Admin e OperadorLogistica.
/// A visibilidade por dono é do serviço, que devolve NaoEncontrado para pedidos de terceiros.
/// </summary>
public sealed class ObterPedidoTestes(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    private static readonly Guid PedidoId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly string Rota = $"/api/v1/pedidos/{PedidoId}";

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
    public async Task Token_sem_nenhum_perfil_responde_403()
    {
        var resposta = await fabrica.CreateClient().ComToken().GetAsync(Rota, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await FormatoErroTestes.DeveSerProblemDetails(resposta, 403, Rota);
    }

    [Fact]
    public async Task Identificador_que_nao_e_uuid_responde_400_e_nao_404()
    {
        var resposta = await fabrica.CreateClient().ComToken(PerfilUsuario.Admin).GetAsync("/api/v1/pedidos/abc", Cancelamento);

        (await EndpointsAutenticacaoTestes.ErrosDe(resposta, "/api/v1/pedidos/abc")).Should().ContainKey("id");
    }

    [Theory]
    [InlineData(PerfilUsuario.Cliente)]
    [InlineData(PerfilUsuario.Admin)]
    [InlineData(PerfilUsuario.OperadorLogistica)]
    public async Task Perfis_autorizados_recebem_200_e_o_servico_recebe_usuario_e_perfis(PerfilUsuario perfil)
    {
        var usuarioId = Guid.NewGuid();
        var falso = new ServicoPedidosFalso { ResultadoObter = Exemplos.Pedido() };
        await using var api = new FabricaApi { ServicoPedidos = falso };
        var cliente = api.CreateClient().ComToken(GeradorTokenTeste.Gerar(usuarioId, [perfil]));

        var resposta = await cliente.GetAsync(Rota, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento)).GetProperty("id").GetGuid().Should().Be(PedidoId);
        falso.UltimoPedidoId.Should().Be(PedidoId);
        falso.UltimoUsuarioId.Should().Be(usuarioId);
        falso.UltimosPerfis.Should().Equal(perfil);
    }

    [Fact]
    public async Task Pedido_inexistente_ou_de_outro_cliente_responde_404()
    {
        var falso = new ServicoPedidosFalso { ResultadoObter = new Falha(TipoFalha.NaoEncontrado, "Pedido não encontrado.") };
        await using var api = new FabricaApi { ServicoPedidos = falso };

        var resposta = await api.CreateClient().ComToken(PerfilUsuario.Cliente).GetAsync(Rota, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        await FormatoErroTestes.DeveSerProblemDetails(resposta, 404, Rota);
    }
}
