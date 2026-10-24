namespace PedidoFulfillment.Application.Reconciliacao;

/// <summary>
/// A regra de um consumidor do fluxo do pedido, aplicável sem a mensagem (REQ-12, R-04): o
/// consumidor a chama ao receber o evento, e a reconciliação a chama para o pedido parado. As
/// reavaliações por conflito, o gateway e o evento de saída ficam num lugar só (FR-008, FR-017).
/// </summary>
public interface IEtapaFluxoPedido
{
    EtapaFluxoPedido Etapa { get; }

    /// <summary>
    /// Aplica a regra ao pedido, gravando o efeito e o evento de saída juntos.
    /// </summary>
    /// <param name="idOrigem">O evento que originou a chamada, para os logs; vazio na reconciliação.</param>
    /// <returns><c>true</c> se gravou o efeito; <c>false</c> se o pedido não estava apto.</returns>
    /// <exception cref="Exception">Qualquer exceção é falha da regra (pedido inexistente, gateway, conflitos persistentes).</exception>
    Task<bool> AplicarAsync(Guid idOrigem, Guid pedidoId, CancellationToken cancellationToken);
}
