using System.Security.Claims;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.API.Seguranca;

/// <summary>
/// Leitura da identidade do token de acesso.
/// </summary>
public static class ExtensoesUsuario
{
    public static Guid ObterUsuarioId(this ClaimsPrincipal usuario) =>
        Guid.TryParse(usuario.FindFirstValue(ConfiguracaoJwt.ClaimUsuario), out var id) ? id : Guid.Empty;

    public static IReadOnlyCollection<PerfilUsuario> ObterPerfis(this ClaimsPrincipal usuario) =>
        usuario.FindAll(ConfiguracaoJwt.ClaimPerfil)
            .Select(c => Enum.TryParse<PerfilUsuario>(c.Value, out var perfil) ? perfil : (PerfilUsuario?)null)
            .OfType<PerfilUsuario>()
            .Distinct()
            .ToArray();
}
