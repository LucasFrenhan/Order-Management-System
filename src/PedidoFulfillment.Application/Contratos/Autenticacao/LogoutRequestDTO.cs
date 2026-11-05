using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Application.Autenticacao;

namespace PedidoFulfillment.Application.Contratos.Autenticacao;

/// <summary>O token de renovação da sessão a encerrar (REQ-24, FR-008).</summary>
public sealed record LogoutRequestDTO
{
    [Required(ErrorMessage = "O token de renovação é obrigatório.")]
    [MaxLength(GeradorTokenOpaco.TamanhoMaximoRecebido, ErrorMessage = "O token de renovação deve ter até 128 caracteres.")]
    public string TokenRenovacao { get; init; } = "";
}
