namespace PedidoFulfillment.Application.Contratos.Pedidos;

/// <summary>
/// Uma página da listagem (REQ-21, FR-015). Sem total nem número de página (clarify); os
/// cursores são nulos quando não há página naquele sentido.
/// </summary>
public sealed record PaginaPedidosResponseDTO
{
    public required IReadOnlyList<ResumoPedidoResponseDTO> Itens { get; init; }

    public required int Tamanho { get; init; }

    /// <summary>Cursor da próxima página; nulo na última.</summary>
    public required string? Proximo { get; init; }

    /// <summary>Cursor da página anterior; nulo na primeira.</summary>
    public required string? Anterior { get; init; }
}
