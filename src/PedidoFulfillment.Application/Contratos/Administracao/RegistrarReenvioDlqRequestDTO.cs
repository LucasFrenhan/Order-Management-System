using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Application.Mensageria;

namespace PedidoFulfillment.Application.Contratos.Administracao;

/// <summary>
/// Filtro de um pedido de reenvio da DLQ (REQ-14, contrato). Todos os campos são opcionais;
/// os informados se combinam. Os textos são medidos depois de aparar.
/// </summary>
public sealed record RegistrarReenvioDlqRequestDTO : IValidatableObject
{
    public string? TipoEvento { get; init; }

    public DateTimeOffset? FalhouDe { get; init; }

    public DateTimeOffset? FalhouAte { get; init; }

    public string? TrechoMotivo { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TipoEvento?.Trim().Length > PedidoReenvioDlq.TamanhoMaximoTipoEvento)
        {
            yield return new ValidationResult($"O tipo de evento deve ter até {PedidoReenvioDlq.TamanhoMaximoTipoEvento} caracteres.", [nameof(TipoEvento)]);
        }

        if (TrechoMotivo?.Trim().Length > PedidoReenvioDlq.TamanhoMaximoTrechoMotivo)
        {
            yield return new ValidationResult($"O trecho do motivo deve ter até {PedidoReenvioDlq.TamanhoMaximoTrechoMotivo} caracteres.", [nameof(TrechoMotivo)]);
        }

        if (FalhouDe is { } de && FalhouAte is { } ate && ate < de)
        {
            yield return new ValidationResult("O fim do período deve ser igual ou posterior ao início.", [nameof(FalhouAte)]);
        }
    }

    public FiltroReenvio ParaFiltro() => new(TipoEvento, FalhouDe, FalhouAte, TrechoMotivo);
}
