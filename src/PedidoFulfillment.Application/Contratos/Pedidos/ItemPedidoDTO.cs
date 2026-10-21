using System.ComponentModel.DataAnnotations;

namespace PedidoFulfillment.Application.Contratos.Pedidos;

/// <summary>
/// Item de entrada de um pedido (FR-017): produto e quantidade. O preço vem do cadastro.
/// </summary>
public sealed record ItemPedidoDTO : IValidatableObject
{
    [Required]
    public Guid ProdutoId { get; init; }

    [Required]
    [Range(1, 999, ErrorMessage = "A quantidade deve estar entre 1 e 999.")]
    public int Quantidade { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ProdutoId == Guid.Empty)
        {
            yield return new ValidationResult("O produto é obrigatório.", [nameof(ProdutoId)]);
        }
    }
}
