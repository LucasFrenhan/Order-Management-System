namespace PedidoFulfillment.Domain.Pedidos;

/// <summary>
/// Transições de status do contrato v1 (FR-020 do REQ-01):
/// <list type="table">
/// <listheader><term>De → Para</term><description>Quem</description></listheader>
/// <item><term>Pendente → Processando</term><description>operação</description></item>
/// <item><term>Processando → ProntoParaEnvio</term><description>operação</description></item>
/// <item><term>ProntoParaEnvio → Despachado</term><description>operação</description></item>
/// <item><term>Pendente, Processando, ProntoParaEnvio → Cancelado</term><description>operação</description></item>
/// <item><term>Pendente → Cancelado</term><description>Cliente dono do pedido</description></item>
/// </list>
/// Despachado, Cancelado e PagamentoRejeitado são finais. Quem é operação (Admin ou
/// OperadorLogistica) e quem é o Cliente dono é decidido pelo serviço (research R-06 do REQ-05).
/// </summary>
public static class TransicoesStatusPedido
{
    public static bool PermitidaParaOperacao(StatusPedido de, StatusPedido para) => (de, para) switch
    {
        (StatusPedido.Pendente, StatusPedido.Processando) => true,
        (StatusPedido.Processando, StatusPedido.ProntoParaEnvio) => true,
        (StatusPedido.ProntoParaEnvio, StatusPedido.Despachado) => true,
        (StatusPedido.Pendente or StatusPedido.Processando or StatusPedido.ProntoParaEnvio, StatusPedido.Cancelado) => true,
        _ => false
    };

    public static bool PermitidaParaCliente(StatusPedido de, StatusPedido para) =>
        de == StatusPedido.Pendente && para == StatusPedido.Cancelado;
}
