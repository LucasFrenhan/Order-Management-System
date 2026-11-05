using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Application.Autenticacao;

namespace PedidoFulfillment.Application.Contratos.Autenticacao;

/// <summary>O token recebido por e-mail (REQ-24, FR-018). Identifica a conta sozinho.</summary>
public sealed record ConfirmacaoEmailRequestDTO
{
    [Required(ErrorMessage = "O token é obrigatório.")]
    [MaxLength(GeradorTokenOpaco.TamanhoMaximoRecebido, ErrorMessage = "O token deve ter até 128 caracteres.")]
    public string Token { get; init; } = "";
}
