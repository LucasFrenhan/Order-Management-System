using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Consumidor de pagamento (REQ-09, <c>specs/009-pagamento/contracts/consumidor-pagamento.md</c>):
/// cobra o pedido com estoque reservado e, conforme o gateway, aprova o pagamento ou o recusa
/// e devolve o estoque. Roda dentro da transação da base de consumo do REQ-06; o resultado sai
/// pelo outbox na mesma gravação.
/// </summary>
public sealed class ProcessamentoPagamento(
    IArmazenamentoPedidos armazenamento, IGatewayPagamento gateway, TimeProvider relogio, ILogger<ProcessamentoPagamento> logger)
    : IManipuladorEvento<EstoqueReservado>
{
    public string Consumidor => "pagamento";

    public IReadOnlyCollection<string> Tipos { get; } = [TiposEvento.EstoqueReservado];

    public Task ManipularAsync(EnvelopeEvento<EstoqueReservado> evento, CancellationToken cancellationToken)
    {
        // Esqueleto da fundação; a cobrança entra na US1 (T012).
        _ = (armazenamento, gateway, relogio, logger);
        throw new NotImplementedException();
    }
}
