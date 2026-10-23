using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Domain.Entregas;

namespace PedidoFulfillment.Application.Contratos.Pedidos;

/// <summary>
/// Transportadora e código de rastreio do despacho (REQ-11, FR-007). Os espaços nas pontas não
/// contam: só espaços é vazio, e o tamanho é medido depois de aparar (research R-09).
/// </summary>
public sealed record RegistrarRastreioRequestDTO : IValidatableObject
{
    [Required]
    [MaxLength(OrdemEntrega.TamanhoMaximoTransportadora)]
    public string Transportadora { get; init; } = "";

    [Required]
    [MaxLength(OrdemEntrega.TamanhoMaximoCodigoRastreio)]
    public string CodigoRastreio { get; init; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Invalido(Transportadora, OrdemEntrega.TamanhoMaximoTransportadora, "A transportadora é obrigatória.", "A transportadora", nameof(Transportadora)) is { } transportadora)
        {
            yield return transportadora;
        }

        if (Invalido(CodigoRastreio, OrdemEntrega.TamanhoMaximoCodigoRastreio, "O código de rastreio é obrigatório.", "O código de rastreio", nameof(CodigoRastreio)) is { } codigo)
        {
            yield return codigo;
        }
    }

    private static ValidationResult? Invalido(string? valor, int maximo, string obrigatorio, string nome, string campo)
    {
        var aparado = valor?.Trim() ?? "";
        if (aparado.Length == 0)
        {
            return new ValidationResult(obrigatorio, [campo]);
        }

        return aparado.Length > maximo ? new ValidationResult($"{nome} deve ter até {maximo} caracteres.", [campo]) : null;
    }
}
