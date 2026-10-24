namespace PedidoFulfillment.Application.Reconciliacao;

/// <summary>Etapa do fluxo automático do pedido em que ele pode ficar parado (REQ-12, data-model).</summary>
public enum EtapaFluxoPedido
{
    Estoque = 1,
    Pagamento,
    Entrega
}
