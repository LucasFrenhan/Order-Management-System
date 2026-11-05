using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Application.Autenticacao;

namespace PedidoFulfillment.Application.Contratos.Autenticacao;

/// <summary>O token de renovação a trocar por um par novo (REQ-24, FR-002).</summary>
public sealed record RenovacaoRequestDTO
{
    [Required(ErrorMessage = "O token de renovação é obrigatório.")]
    [MaxLength(GeradorTokenOpaco.TamanhoMaximoRecebido, ErrorMessage = "O token de renovação deve ter até 128 caracteres.")]
    public string TokenRenovacao { get; init; } = "";
}
