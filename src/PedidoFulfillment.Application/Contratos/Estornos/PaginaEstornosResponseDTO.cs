namespace PedidoFulfillment.Application.Contratos.Estornos;

/// <summary>Uma página dos estornos pendentes, como as de usuários e produtos (REQ-28, FR-003).</summary>
public sealed record PaginaEstornosResponseDTO
{
    public required IReadOnlyList<EstornoResponseDTO> Itens { get; init; }

    public required int Tamanho { get; init; }

    /// <summary>Cursor da próxima página; nulo na última.</summary>
    public required string? Proximo { get; init; }

    /// <summary>Cursor da página anterior; nulo na primeira.</summary>
    public required string? Anterior { get; init; }
}
