namespace PedidoFulfillment.Application.Contratos.Produtos;

/// <summary>Uma página da listagem de produtos, como a de pedidos (REQ-22, FR-006).</summary>
public sealed record PaginaProdutosResponseDTO
{
    public required IReadOnlyList<ProdutoResponseDTO> Itens { get; init; }

    public required int Tamanho { get; init; }

    /// <summary>Cursor da próxima página; nulo na última.</summary>
    public required string? Proximo { get; init; }

    /// <summary>Cursor da página anterior; nulo na primeira.</summary>
    public required string? Anterior { get; init; }
}
