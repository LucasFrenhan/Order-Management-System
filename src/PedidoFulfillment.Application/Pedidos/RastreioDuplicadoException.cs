namespace PedidoFulfillment.Application.Pedidos;

/// <summary>
/// A gravação foi recusada porque outro pedido já usa o mesmo par transportadora e código de
/// rastreio (REQ-11, research R-06). Nada foi gravado; o serviço responde o conflito.
/// </summary>
public sealed class RastreioDuplicadoException(Exception? excecaoInterna = null)
    : Exception("O código de rastreio já é usado por outro pedido da mesma transportadora.", excecaoInterna);
