namespace PedidoFulfillment.Application.Contratos.Pedidos;

/// <summary>
/// Ordem de entrega do pedido (REQ-11, FR-004). Transportadora, código de rastreio e data de
/// despacho ficam nulos até o despacho.
/// </summary>
public sealed record EntregaPedidoResponseDTO
{
    public string? Transportadora { get; init; }

    public string? CodigoRastreio { get; init; }

    public DateTimeOffset? DespachadoEm { get; init; }

    public required DateTimeOffset CriadoEm { get; init; }
}
