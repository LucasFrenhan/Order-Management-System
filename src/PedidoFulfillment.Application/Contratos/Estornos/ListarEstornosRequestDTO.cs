using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace PedidoFulfillment.Application.Contratos.Estornos;

/// <summary>
/// Parâmetros de <c>GET /api/v1/estornos/pendentes</c> (REQ-28, R-08), como texto, como as demais
/// listagens. O cursor é conferido no serviço.
/// </summary>
public sealed record ListarEstornosRequestDTO : IValidatableObject
{
    public const int TamanhoPadrao = 20;
    public const int TamanhoMaximo = 100;

    public string? Tamanho { get; init; }

    public string? Cursor { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Tamanho is not null && !TentarTamanho(Tamanho, out _))
        {
            yield return new ValidationResult($"O tamanho deve ser um número entre 1 e {TamanhoMaximo}.", ["tamanho"]);
        }
    }

    /// <summary>Tamanho da página; o padrão quando ausente. Só depois da validação.</summary>
    public int TamanhoPagina() => Tamanho is null ? TamanhoPadrao : int.Parse(Tamanho, NumberStyles.None, CultureInfo.InvariantCulture);

    private static bool TentarTamanho(string texto, out int tamanho) =>
        int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out tamanho) && tamanho is >= 1 and <= TamanhoMaximo;
}
