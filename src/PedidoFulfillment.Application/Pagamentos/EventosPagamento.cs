using Microsoft.Extensions.Logging;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Eventos do pagamento (REQ-09, research R-08). Só identificadores, valores e situação, sem
/// dados pessoais (FR-014).
/// </summary>
internal static partial class EventosPagamento
{
    [LoggerMessage(EventId = 7001, EventName = "PagamentoAprovado", Level = LogLevel.Information,
        Message = "Pagamento aprovado para o pedido {PedidoId} (valor {Valor}, transação {IdentificadorTransacao}), evento {IdEvento}.")]
    public static partial void PagamentoAprovado(ILogger logger, Guid idEvento, Guid pedidoId, decimal valor, string identificadorTransacao);

    [LoggerMessage(EventId = 7002, EventName = "PagamentoRecusado", Level = LogLevel.Information,
        Message = "Pagamento recusado para o pedido {PedidoId} (valor {Valor}); estoque devolvido, evento {IdEvento}.")]
    public static partial void PagamentoRecusado(ILogger logger, Guid idEvento, Guid pedidoId, decimal valor);

    [LoggerMessage(EventId = 7003, EventName = "PedidoIgnoradoNoPagamento", Level = LogLevel.Information,
        Message = "Pedido {PedidoId} ignorado no pagamento: status {Status}, estoque reservado: {EstoqueReservado}, já pago: {JaPago}; evento {IdEvento}.")]
    public static partial void PedidoIgnoradoNoPagamento(ILogger logger, Guid idEvento, Guid pedidoId, StatusPedido status, bool estoqueReservado, bool jaPago);

    [LoggerMessage(EventId = 7004, EventName = "ConflitoNoPagamento", Level = LogLevel.Warning,
        Message = "Conflito de concorrência no pagamento do pedido {PedidoId} (reavaliação {Reavaliacao}), evento {IdEvento}.")]
    public static partial void ConflitoNoPagamento(ILogger logger, Guid idEvento, Guid pedidoId, int reavaliacao);
}
