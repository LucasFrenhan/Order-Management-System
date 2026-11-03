using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Domain.Entregas;

namespace PedidoFulfillment.Application.Contratos.Pedidos;

/// <summary>
/// Transportadora e código de rastreio do despacho (REQ-11, FR-007). Os espaços nas pontas não
/// contam: os valores são aparados ao chegar, então só espaços é vazio e o tamanho é medido
/// depois de aparar (research R-09). Os atributos validam e também descrevem o schema.
/// </summary>
public sealed record RegistrarRastreioRequestDTO
{
    [Required(ErrorMessage = "A transportadora é obrigatória.")]
    [MaxLength(OrdemEntrega.TamanhoMaximoTransportadora, ErrorMessage = "A transportadora deve ter até 100 caracteres.")]
    public string Transportadora { get; init => field = value?.Trim() ?? ""; } = "";

    [Required(ErrorMessage = "O código de rastreio é obrigatório.")]
    [MaxLength(OrdemEntrega.TamanhoMaximoCodigoRastreio, ErrorMessage = "O código de rastreio deve ter até 50 caracteres.")]
    public string CodigoRastreio { get; init => field = value?.Trim() ?? ""; } = "";
}
