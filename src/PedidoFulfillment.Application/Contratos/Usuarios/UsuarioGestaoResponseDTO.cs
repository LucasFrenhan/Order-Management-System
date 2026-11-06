using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Contratos.Usuarios;

/// <summary>
/// Conta vista pelo Admin (REQ-25, FR-006). Nunca traz senha, hash, carimbo de segurança nem token.
/// </summary>
public sealed record UsuarioGestaoResponseDTO
{
    public required Guid Id { get; init; }

    public required string Nome { get; init; }

    [EmailAddress]
    public required string Email { get; init; }

    public required IReadOnlyList<PerfilUsuario> Perfis { get; init; }

    public required SituacaoUsuario Situacao { get; init; }

    public required bool EmailConfirmado { get; init; }

    /// <summary>A conta ainda não definiu a senha.</summary>
    public required bool ConvitePendente { get; init; }
}
