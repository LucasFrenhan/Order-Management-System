namespace PedidoFulfillment.Application.Contratos.Pedidos;

/// <summary>
/// Item de um pedido criado, com o preço do cadastro no momento da criação.
/// </summary>
public sealed record ItemPedidoResponseDTO
{
    public required Guid ProdutoId { get; init; }

    public required string NomeProduto { get; init; }

    public required int Quantidade { get; init; }

    /// <summary>Duas casas decimais.</summary>
    public required decimal PrecoUnitario { get; init; }

    /// <summary><c>PrecoUnitario × Quantidade</c>, com duas casas decimais.</summary>
    public required decimal Subtotal { get; init; }
}
