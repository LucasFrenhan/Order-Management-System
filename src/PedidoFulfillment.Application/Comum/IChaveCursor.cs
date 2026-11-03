namespace PedidoFulfillment.Application.Comum;

/// <summary>
/// Chave do HMAC dos cursores das listagens de pedidos (REQ-21) e de produtos (REQ-22), research
/// R-04 do REQ-21. A implementação de hoje deriva a chave da chave de assinatura do JWT; ela pode
/// passar a uma chave própria do cursor trocando só o registro desta interface na API. A troca
/// invalida os cursores em uso (400). Chamava-se <c>IChaveCursorPedidos</c> até o REQ-22.
/// </summary>
public interface IChaveCursor
{
    /// <summary>Bytes da chave; ao menos 32.</summary>
    byte[] Chave { get; }
}
