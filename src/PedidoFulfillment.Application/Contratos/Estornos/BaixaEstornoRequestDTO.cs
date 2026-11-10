using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Domain.Pagamentos;

namespace PedidoFulfillment.Application.Contratos.Estornos;

/// <summary>
/// Baixa manual do estorno (REQ-28, FR-006). Os espaços nas pontas não contam: os valores são aparados
/// ao chegar, e a referência só com espaços vira ausente. Os atributos validam e também descrevem o schema.
/// </summary>
public sealed record BaixaEstornoRequestDTO
{
    public const string MensagemMotivo = "O motivo deve ter entre 10 e 500 caracteres, sem contar espaços nas pontas.";

    [Required(ErrorMessage = "O motivo é obrigatório.")]
    [MinLength(Pagamento.TamanhoMinimoMotivoBaixa, ErrorMessage = MensagemMotivo)]
    [MaxLength(Pagamento.TamanhoMaximoMotivoBaixa, ErrorMessage = MensagemMotivo)]
    public string Motivo { get; init => field = value?.Trim() ?? ""; } = "";

    [MaxLength(Pagamento.TamanhoMaximoReferenciaExternaBaixa, ErrorMessage = "A referência externa deve ter até 100 caracteres, sem contar espaços nas pontas.")]
    public string? ReferenciaExterna { get; init => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
}
