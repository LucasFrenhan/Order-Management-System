using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.Application.Estoque;

/// <summary>
/// Consumidor de estoque (REQ-08, <c>specs/008-estoque/contracts/consumidor-estoque.md</c>):
/// reserva o estoque do pedido criado ou, sem estoque, cancela o pedido. Roda dentro da
/// transação da base de consumo do REQ-06.
/// </summary>
public sealed class ReservaEstoque(IArmazenamentoPedidos armazenamento, TimeProvider relogio, ILogger<ReservaEstoque> logger)
    : IManipuladorEvento<PedidoCriado>
{
    public string Consumidor => "estoque";

    public IReadOnlyCollection<string> Tipos { get; } = [TiposEvento.PedidoCriado];

    public Task ManipularAsync(EnvelopeEvento<PedidoCriado> evento, CancellationToken cancellationToken)
    {
        // Esqueleto da fundação; a reserva entra na US1 (T012).
        _ = (armazenamento, relogio, logger);
        throw new NotImplementedException();
    }
}
