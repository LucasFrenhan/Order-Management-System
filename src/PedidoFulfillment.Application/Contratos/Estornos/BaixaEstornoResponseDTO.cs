namespace PedidoFulfillment.Application.Contratos.Estornos;

/// <summary>Dados da baixa manual (REQ-28, FR-007).</summary>
public sealed record BaixaEstornoResponseDTO
{
    public required Guid AutorId { get; init; }

    public required DateTimeOffset BaixadoEm { get; init; }

    public required string Motivo { get; init; }

    public required string? ReferenciaExterna { get; init; }
}
