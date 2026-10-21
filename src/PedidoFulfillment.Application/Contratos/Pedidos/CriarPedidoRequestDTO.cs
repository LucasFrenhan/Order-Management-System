using System.ComponentModel.DataAnnotations;

namespace PedidoFulfillment.Application.Contratos.Pedidos;

/// <summary>
/// Pedido a criar (FR-016). O cliente não informa preço, total, status nem o próprio
/// identificador: esses dados são definidos pelo sistema.
/// </summary>
public sealed record CriarPedidoRequestDTO : IValidatableObject
{
    [Required(ErrorMessage = "O pedido deve ter entre 1 e 50 itens.")]
    [MinLength(1, ErrorMessage = "O pedido deve ter entre 1 e 50 itens.")]
    [MaxLength(50, ErrorMessage = "O pedido deve ter entre 1 e 50 itens.")]
    public IReadOnlyList<ItemPedidoDTO> Itens { get; init; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Itens.GroupBy(i => i.ProdutoId).Any(g => g.Count() > 1))
        {
            yield return new ValidationResult("Cada produto pode aparecer no máximo uma vez.", [nameof(Itens)]);
        }
    }
}
