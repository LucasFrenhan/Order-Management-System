using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Contratos.Autenticacao;

/// <summary>
/// Usuário criado (FR-013). Nunca expõe senha nem hash (FR-008).
/// </summary>
public sealed record UsuarioResponseDTO
{
    public required Guid Id { get; init; }

    public required string Nome { get; init; }

    [EmailAddress]
    public required string Email { get; init; }

    [MinLength(1)]
    public required IReadOnlyList<PerfilUsuario> Perfis { get; init; }
}
