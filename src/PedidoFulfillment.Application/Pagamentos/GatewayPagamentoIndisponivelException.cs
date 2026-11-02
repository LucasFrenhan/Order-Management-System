using PedidoFulfillment.Application.Mensageria;

namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// O circuito do gateway de pagamento está aberto (REQ-19, FR-016 e FR-016a): a chamada nem saiu.
/// O consumidor de pagamento pausa, o estorno fica adiado e a reconciliação conta falha do ciclo.
/// </summary>
public sealed class GatewayPagamentoIndisponivelException(DateTimeOffset disponivelEm, Exception? innerException = null)
    : DependenciaIndisponivelException("O gateway de pagamento está indisponível (circuito aberto).", disponivelEm, innerException);
