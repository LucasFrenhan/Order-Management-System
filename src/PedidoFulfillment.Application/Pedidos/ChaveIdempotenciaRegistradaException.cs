namespace PedidoFulfillment.Application.Pedidos;

/// <summary>
/// A gravação foi recusada porque outra requisição registrou o mesmo par (cliente, chave)
/// antes (research R-04 do REQ-05). Nada foi gravado; o serviço relê o registro vencedor.
/// </summary>
public sealed class ChaveIdempotenciaRegistradaException(Exception? excecaoInterna = null)
    : Exception("A chave de idempotência já foi registrada por outra requisição.", excecaoInterna);
