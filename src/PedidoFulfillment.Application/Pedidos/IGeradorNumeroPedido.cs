namespace PedidoFulfillment.Application.Pedidos;

/// <summary>
/// Número legível do pedido, <c>PED-AAAAMMDD-NNNNNN</c>, único mesmo entre criações
/// simultâneas (FR-005, research R-05 do REQ-05).
/// </summary>
public interface IGeradorNumeroPedido
{
    /// <summary>Próximo número, com a data UTC de <paramref name="instante"/>.</summary>
    Task<string> ProximoAsync(DateTimeOffset instante, CancellationToken cancellationToken);
}
