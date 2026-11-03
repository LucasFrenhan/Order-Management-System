using System.ComponentModel.DataAnnotations;

namespace PedidoFulfillment.Application.Contratos.Produtos;

/// <summary>
/// Preço do catálogo: maior que zero, até 999999.99, com no máximo duas casas (REQ-22, FR-011).
/// Atributo de propriedade, e não <see cref="IValidatableObject"/>, para entrar na mesma rodada
/// dos demais e o 400 trazer todos os erros juntos (FR-020): o <c>Validator</c> só chama o
/// <c>Validate</c> quando os atributos passam.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class PrecoCatalogoAttribute() : ValidationAttribute(MensagensCatalogo.Preco)
{
    public override bool IsValid(object? value) => value is decimal preco && MensagensCatalogo.PrecoValido(preco);
}

/// <summary>Variação de estoque diferente de zero e dentro do limite (REQ-22, FR-014).</summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class VariacaoEstoqueAttribute() : ValidationAttribute(MensagensCatalogo.Variacao)
{
    public override bool IsValid(object? value) =>
        value is int variacao && variacao != 0 && Math.Abs((long)variacao) <= MensagensCatalogo.VariacaoMaxima;
}
