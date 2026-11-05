using System.ComponentModel.DataAnnotations;

namespace PedidoFulfillment.Application.Contratos.Autenticacao;

/// <summary>O e-mail da conta a recuperar (REQ-24, FR-013). A resposta é igual com ou sem conta.</summary>
public sealed record RecuperacaoSenhaRequestDTO
{
    [Required(ErrorMessage = "O e-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "O e-mail deve ter um formato válido.")]
    [MaxLength(256, ErrorMessage = "O e-mail deve ter até 256 caracteres.")]
    public string Email { get; init; } = "";
}
