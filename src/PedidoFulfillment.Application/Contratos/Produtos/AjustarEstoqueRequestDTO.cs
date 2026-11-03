using System.ComponentModel.DataAnnotations;

namespace PedidoFulfillment.Application.Contratos.Produtos;

/// <summary>
/// Ajuste de estoque por variação (REQ-22, FR-014): positiva repõe, negativa dá baixa. O motivo vai
/// ao log, que é o histórico dos ajustes (clarify).
/// </summary>
public sealed record AjustarEstoqueRequestDTO
{
    [Range(-MensagensCatalogo.VariacaoMaxima, MensagensCatalogo.VariacaoMaxima, ErrorMessage = MensagensCatalogo.Variacao)]
    [VariacaoEstoque]
    public int Variacao { get; init; }

    [Required(ErrorMessage = MensagensCatalogo.Motivo)]
    [MaxLength(MensagensCatalogo.TamanhoMaximoMotivo, ErrorMessage = MensagensCatalogo.Motivo)]
    public string Motivo { get; init => field = value?.Trim() ?? ""; } = "";
}
