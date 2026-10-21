namespace PedidoFulfillment.Domain.Pedidos;

/// <summary>
/// Estados do ciclo de vida do pedido (FR-021). Trafega como texto no contrato.
/// </summary>
/// <remarks>
/// A numeração começa em 1 para que um status ausente na requisição (valor 0) seja
/// detectado como inválido em vez de virar <see cref="Pendente"/> silenciosamente.
/// </remarks>
public enum StatusPedido
{
    Pendente = 1,
    Processando,
    ProntoParaEnvio,
    Despachado,
    Cancelado,
    PagamentoRejeitado
}
