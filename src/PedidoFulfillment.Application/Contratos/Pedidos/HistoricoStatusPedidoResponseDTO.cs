using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Contratos.Pedidos;

/// <summary>
/// Uma mudança de status do pedido (REQ-11, FR-002). Sem o autor, para nenhum perfil (FR-003).
/// </summary>
public sealed record HistoricoStatusPedidoResponseDTO
{
    /// <summary>Nulo na criação do pedido.</summary>
    public StatusPedido? StatusAnterior { get; init; }

    public required StatusPedido StatusNovo { get; init; }

    public required DateTimeOffset OcorridoEm { get; init; }

    public string? Motivo { get; init; }
}
