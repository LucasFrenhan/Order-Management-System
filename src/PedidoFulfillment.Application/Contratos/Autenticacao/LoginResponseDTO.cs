using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Contratos.Autenticacao;

/// <summary>
/// Token de acesso emitido no login (FR-015). Vale 60 minutos a partir da emissão.
/// </summary>
public sealed record LoginResponseDTO
{
    public required string TokenAcesso { get; init; }

    // Sempre "Bearer"; [Required] mantém o campo obrigatório no schema sem exigir que o serviço o informe.
    [Required]
    public string TipoToken { get; init; } = "Bearer";

    public required DateTimeOffset ExpiraEm { get; init; }

    public required IReadOnlyList<PerfilUsuario> Perfis { get; init; }
}
