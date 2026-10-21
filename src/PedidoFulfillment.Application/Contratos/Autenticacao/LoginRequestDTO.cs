using System.ComponentModel.DataAnnotations;

namespace PedidoFulfillment.Application.Contratos.Autenticacao;

/// <summary>
/// Credenciais de login (FR-014).
/// </summary>
public sealed record LoginRequestDTO
{
    [Required(ErrorMessage = "O e-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "O e-mail deve ter um formato válido.")]
    public string Email { get; init; } = "";

    [Required(ErrorMessage = "A senha é obrigatória.")]
    [DataType(DataType.Password)]
    public string Senha { get; init; } = "";
}
