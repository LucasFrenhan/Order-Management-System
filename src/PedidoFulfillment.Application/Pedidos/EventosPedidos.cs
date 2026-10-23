using Microsoft.Extensions.Logging;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Pedidos;

/// <summary>
/// Eventos de pedidos (research R-11 do REQ-05). Nunca recebem e-mail, token nem o corpo da
/// requisição (FR-020).
/// </summary>
internal static partial class EventosPedidos
{
    [LoggerMessage(EventId = 4001, EventName = "PedidoCriado", Level = LogLevel.Information,
        Message = "Pedido {PedidoId} ({Numero}) criado com {QuantidadeItens} item(ns), total {ValorTotal}.")]
    public static partial void PedidoCriado(ILogger logger, Guid pedidoId, string numero, int quantidadeItens, decimal valorTotal);

    [LoggerMessage(EventId = 4002, EventName = "PedidoRepetido", Level = LogLevel.Information,
        Message = "Criação repetida com a mesma chave; devolvida a resposta original do pedido {PedidoId}.")]
    public static partial void PedidoRepetido(ILogger logger, Guid pedidoId);

    /// <summary><paramref name="papelAutor"/>: <c>Operacao</c> ou <c>Cliente</c>, o papel que decidiu a regra.</summary>
    [LoggerMessage(EventId = 4003, EventName = "StatusPedidoAlterado", Level = LogLevel.Information,
        Message = "Pedido {PedidoId} passou de {StatusAnterior} para {StatusNovo} ({PapelAutor}).")]
    public static partial void StatusPedidoAlterado(ILogger logger, Guid pedidoId, StatusPedido statusAnterior, StatusPedido statusNovo, string papelAutor);

    /// <summary>Despacho ou correção do rastreio (REQ-11, FR-016); sem transportadora nem código.</summary>
    [LoggerMessage(EventId = 4004, EventName = "RastreioRegistrado", Level = LogLevel.Information,
        Message = "Rastreio do pedido {PedidoId} registrado pelo usuário {UsuarioId}: {Resultado}.")]
    public static partial void RastreioRegistrado(ILogger logger, Guid pedidoId, Guid usuarioId, ResultadoRastreio resultado);

    [LoggerMessage(EventId = 4005, EventName = "RastreioDuplicado", Level = LogLevel.Information,
        Message = "Rastreio do pedido {PedidoId} recusado para o usuário {UsuarioId}: já usado por outro pedido da mesma transportadora.")]
    public static partial void RastreioDuplicado(ILogger logger, Guid pedidoId, Guid usuarioId);
}
