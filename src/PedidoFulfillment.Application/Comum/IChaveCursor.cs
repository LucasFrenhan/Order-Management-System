namespace PedidoFulfillment.Application.Comum;

/// <summary>
/// Chave do HMAC dos cursores das listagens de pedidos (REQ-21), de produtos (REQ-22) e de usuários (REQ-25),
/// research R-04 do REQ-21. Na API, vem da chave própria opcional <c>Listagens:ChaveCursor</c> ou, sem ela, da chave
/// ativa do JWT (REQ-27, research R-06). Trocar a chave invalida os cursores em uso (400). Chamava-se
/// <c>IChaveCursorPedidos</c> até o REQ-22.
/// </summary>
public interface IChaveCursor
{
    /// <summary>Bytes da chave; ao menos 32.</summary>
    byte[] Chave { get; }
}
