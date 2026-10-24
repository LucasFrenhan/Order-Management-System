using Microsoft.Extensions.Logging;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Entregas;

/// <summary>
/// Eventos da entrega (REQ-10, research R-08). Só identificadores e situação, sem dados pessoais
/// (FR-011).
/// </summary>
internal static partial class EventosEntrega
{
    [LoggerMessage(EventId = 8001, EventName = "OrdemEntregaCriada", Level = LogLevel.Information,
        Message = "Ordem de entrega {OrdemEntregaId} criada para o pedido {PedidoId} (status mudou: {StatusMudou}), evento {IdEvento}.")]
    public static partial void OrdemEntregaCriada(ILogger logger, Guid idEvento, Guid pedidoId, Guid ordemEntregaId, bool statusMudou);

    [LoggerMessage(EventId = 8002, EventName = "PedidoIgnoradoNaEntrega", Level = LogLevel.Information,
        Message = "Pedido {PedidoId} ignorado na entrega: status {Status}, estoque reservado: {EstoqueReservado}, pagamento aprovado: {PagamentoAprovado}, já tem ordem: {JaTemOrdem}; evento {IdEvento}.")]
    public static partial void PedidoIgnoradoNaEntrega(
        ILogger logger, Guid idEvento, Guid pedidoId, StatusPedido status, bool estoqueReservado, bool pagamentoAprovado, bool jaTemOrdem);

    [LoggerMessage(EventId = 8003, EventName = "ConflitoNaEntrega", Level = LogLevel.Warning,
        Message = "Conflito de concorrência na entrega do pedido {PedidoId} (reavaliação {Reavaliacao}), evento {IdEvento}.")]
    public static partial void ConflitoNaEntrega(ILogger logger, Guid idEvento, Guid pedidoId, int reavaliacao);

    [LoggerMessage(EventId = 8004, EventName = "OrdemEntregaCancelada", Level = LogLevel.Information,
        Message = "Ordem de entrega do pedido {PedidoId} cancelada junto com o pedido.")]
    public static partial void OrdemEntregaCancelada(ILogger logger, Guid pedidoId);
}
