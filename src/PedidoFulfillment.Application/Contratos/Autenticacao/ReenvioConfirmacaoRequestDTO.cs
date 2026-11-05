using System.ComponentModel.DataAnnotations;

namespace PedidoFulfillment.Application.Contratos.Autenticacao;

/// <summary>O e-mail a confirmar (REQ-24, FR-019). A resposta é igual em qualquer caso.</summary>
public sealed record ReenvioConfirmacaoRequestDTO
{
    [Required(ErrorMessage = "O e-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "O e-mail deve ter um formato válido.")]
    [MaxLength(256, ErrorMessage = "O e-mail deve ter até 256 caracteres.")]
    public string Email { get; init; } = "";
}
