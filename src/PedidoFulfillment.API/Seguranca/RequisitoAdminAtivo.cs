using Microsoft.AspNetCore.Authorization;
using PedidoFulfillment.Application.Usuarios;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.API.Seguranca;

/// <summary>
/// Quem chama ainda é um Admin ativo, conferido no banco a cada chamada (REQ-25, FR-002): um Admin
/// removido perde a gestão na hora, mesmo com o token válido. Só as rotas de gestão consultam o banco
/// por requisição (R-02).
/// </summary>
public sealed class RequisitoAdminAtivo : IAuthorizationRequirement;

/// <summary>Só delega ao gestor; sem sucesso aqui, a resposta é o 403 de sempre.</summary>
public sealed class ManipuladorAdminAtivo(IGestorUsuarios gestor, IHttpContextAccessor http) : AuthorizationHandler<RequisitoAdminAtivo>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, RequisitoAdminAtivo requirement)
    {
        // Sem o perfil no token, o 403 sai sem consultar o banco: nem Cliente gera consulta, nem banco fora do ar vira 500.
        if (!context.User.ObterPerfis().Contains(PerfilUsuario.Admin))
        {
            return;
        }

        var cancellationToken = http.HttpContext?.RequestAborted ?? CancellationToken.None;
        if (await gestor.EhAdminAtivoAsync(context.User.ObterUsuarioId(), cancellationToken))
        {
            context.Succeed(requirement);
        }
    }
}
