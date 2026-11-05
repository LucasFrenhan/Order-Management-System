using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Contratos.Autenticacao;

/// <summary>
/// Tokens emitidos no login e na renovação (FR-015; REQ-24, FR-001). A validade do token de acesso é
/// configurável, 15 minutos por padrão: o cliente deve ler <see cref="ExpiraEm"/>.
/// </summary>
public sealed record LoginResponseDTO
{
    public required string TokenAcesso { get; init; }

    // Sempre "Bearer"; [Required] mantém o campo obrigatório no schema sem exigir que o serviço o informe.
    [Required]
    public string TipoToken { get; init; } = "Bearer";

    public required DateTimeOffset ExpiraEm { get; init; }

    public required IReadOnlyList<PerfilUsuario> Perfis { get; init; }

    // O emissor do JWT cuida só do token de acesso, e o serviço de sessões completa estes dois (R-06).
    [Required]
    public string TokenRenovacao { get; init; } = "";

    [Required]
    public DateTimeOffset TokenRenovacaoExpiraEm { get; init; }
}
