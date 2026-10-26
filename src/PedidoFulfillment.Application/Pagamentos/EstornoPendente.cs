namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Pedido com estorno pendente para a rodada (REQ-13, R-04), com o contexto de rastreamento da
/// criação (REQ-16, R-05), opaco aqui: só liga o passo da rodada ao pedido.
/// </summary>
public sealed record EstornoPendente(Guid PedidoId, string? ContextoRastreio);
