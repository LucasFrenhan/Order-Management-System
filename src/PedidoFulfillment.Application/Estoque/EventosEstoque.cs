using Microsoft.Extensions.Logging;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Estoque;

/// <summary>
/// Eventos da reserva e da devolução de estoque (REQ-08, research R-09). Só identificadores e
/// quantidades, sem dados pessoais (FR-015).
/// </summary>
internal static partial class EventosEstoque
{
    [LoggerMessage(EventId = 6001, EventName = "EstoqueReservado", Level = LogLevel.Information,
        Message = "Estoque reservado para o pedido {PedidoId} ({QuantidadeItens} item(ns)), evento {IdEvento}.")]
    public static partial void EstoqueReservado(ILogger logger, Guid idEvento, Guid pedidoId, int quantidadeItens);

    [LoggerMessage(EventId = 6002, EventName = "PedidoCanceladoSemEstoque", Level = LogLevel.Information,
        Message = "Pedido {PedidoId} cancelado por falta de estoque ({ItensEmFalta} item(ns) em falta), evento {IdEvento}.")]
    public static partial void PedidoCanceladoSemEstoque(ILogger logger, Guid idEvento, Guid pedidoId, int itensEmFalta);

    [LoggerMessage(EventId = 6003, EventName = "PedidoIgnoradoNaReserva", Level = LogLevel.Information,
        Message = "Pedido {PedidoId} ignorado na reserva: status {Status}, estoque já reservado: {JaReservado}; evento {IdEvento}.")]
    public static partial void PedidoIgnoradoNaReserva(ILogger logger, Guid idEvento, Guid pedidoId, StatusPedido status, bool jaReservado);

    [LoggerMessage(EventId = 6004, EventName = "ConflitoNaReserva", Level = LogLevel.Warning,
        Message = "Conflito de concorrência na reserva do pedido {PedidoId} (reavaliação {Reavaliacao}), evento {IdEvento}.")]
    public static partial void ConflitoNaReserva(ILogger logger, Guid idEvento, Guid pedidoId, int reavaliacao);

    [LoggerMessage(EventId = 6005, EventName = "EstoqueDevolvido", Level = LogLevel.Information,
        Message = "Estoque devolvido no cancelamento do pedido {PedidoId} ({QuantidadeItens} item(ns)).")]
    public static partial void EstoqueDevolvido(ILogger logger, Guid pedidoId, int quantidadeItens);
}
