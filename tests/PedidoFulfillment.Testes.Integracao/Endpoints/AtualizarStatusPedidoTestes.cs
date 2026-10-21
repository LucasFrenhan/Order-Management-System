using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Erros;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Endpoints;

/// <summary>
/// User Story 5: PATCH /api/v1/pedidos/{id}/status. A matriz de transições por perfil (FR-020)
/// é do serviço; a rota valida o corpo e repassa usuário e perfis.
/// </summary>
public sealed class AtualizarStatusPedidoTestes(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    private static readonly Guid PedidoId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private static readonly string Rota = $"/api/v1/pedidos/{PedidoId}/status";
    private static readonly object CancelamentoValido = new { status = "Cancelado", motivo = "Cliente desistiu da compra." };

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sem_token_responde_401()
    {
        var resposta = await fabrica.CreateClient().PatchAsJsonAsync(Rota, CancelamentoValido, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await FormatoErroTestes.DeveSerProblemDetails(resposta, 401, Rota);
    }

    [Fact]
    public async Task Token_sem_nenhum_perfil_responde_403()
    {
        var resposta = await fabrica.CreateClient().ComToken().PatchAsJsonAsync(Rota, CancelamentoValido, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await FormatoErroTestes.DeveSerProblemDetails(resposta, 403, Rota);
    }

    [Theory]
    [InlineData("""{"status":"Inexistente"}""")]
    [InlineData("""{}""")]
    [InlineData("""{"status":"PagamentoRejeitado"}""")]
    public async Task Status_invalido_ausente_ou_proibido_responde_400_em_status(string corpo)
    {
        var resposta = await fabrica.CreateClient().ComToken(PerfilUsuario.Admin).PatchAsync(Rota, Json(corpo), Cancelamento);

        (await EndpointsAutenticacaoTestes.ErrosDe(resposta, Rota)).Should().ContainKey("status");
    }

    [Fact]
    public async Task Cancelamento_sem_motivo_responde_400_em_motivo()
    {
        var resposta = await fabrica.CreateClient().ComToken(PerfilUsuario.Admin)
            .PatchAsync(Rota, Json("""{"status":"Cancelado"}"""), Cancelamento);

        (await EndpointsAutenticacaoTestes.ErrosDe(resposta, Rota)).Should().ContainKey("motivo");
    }

    [Theory]
    [InlineData(PerfilUsuario.Cliente)]
    [InlineData(PerfilUsuario.Admin)]
    [InlineData(PerfilUsuario.OperadorLogistica)]
    public async Task Atualizacao_permitida_responde_200_e_repassa_usuario_perfis_e_dto(PerfilUsuario perfil)
    {
        var usuarioId = Guid.NewGuid();
        var falso = new ServicoPedidosFalso { ResultadoAtualizarStatus = Exemplos.Pedido(StatusPedido.Cancelado) };
        await using var api = new FabricaApi { ServicoPedidos = falso };
        var cliente = api.CreateClient().ComToken(GeradorTokenTeste.Gerar(usuarioId, [perfil]));

        var resposta = await cliente.PatchAsJsonAsync(Rota, CancelamentoValido, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento)).GetProperty("status").GetString().Should().Be("Cancelado");
        falso.UltimoPedidoId.Should().Be(PedidoId);
        falso.UltimoUsuarioId.Should().Be(usuarioId);
        falso.UltimosPerfis.Should().Equal(perfil);
        falso.UltimaAtualizacao!.Status.Should().Be(StatusPedido.Cancelado);
        falso.UltimaAtualizacao.Motivo.Should().Be("Cliente desistiu da compra.");
    }

    [Fact]
    public async Task Transicao_invalida_responde_409_com_codigo()
    {
        var falso = new ServicoPedidosFalso
        {
            ResultadoAtualizarStatus = new Falha(TipoFalha.Conflito, "Transição de status não permitida.",
                "O pedido está Despachado e não pode ir para Cancelado.", CodigosErro.PedidoTransicaoInvalida)
        };
        await using var api = new FabricaApi { ServicoPedidos = falso };

        var resposta = await api.CreateClient().ComToken(PerfilUsuario.Admin).PatchAsJsonAsync(Rota, CancelamentoValido, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var corpo = await FormatoErroTestes.DeveSerProblemDetails(resposta, 409, Rota);
        corpo.GetProperty("codigo").GetString().Should().Be("PEDIDO_TRANSICAO_INVALIDA");
    }

    [Theory]
    [InlineData(TipoFalha.SemPermissao, 403)]
    [InlineData(TipoFalha.NaoEncontrado, 404)]
    public async Task Cliente_sem_permissao_ou_pedido_de_terceiro_respondem_403_ou_404(TipoFalha tipo, int status)
    {
        var falso = new ServicoPedidosFalso { ResultadoAtualizarStatus = new Falha(tipo, "Falha.") };
        await using var api = new FabricaApi { ServicoPedidos = falso };

        var resposta = await api.CreateClient().ComToken(PerfilUsuario.Cliente).PatchAsJsonAsync(Rota, CancelamentoValido, Cancelamento);

        ((int)resposta.StatusCode).Should().Be(status);
        await FormatoErroTestes.DeveSerProblemDetails(resposta, status, Rota);
    }

    private static StringContent Json(string corpo) => new(corpo, System.Text.Encoding.UTF8, "application/json");
}
