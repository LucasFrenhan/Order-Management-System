namespace PedidoFulfillment.Domain.Pedidos;

/// <summary>
/// O que o cancelamento fez junto com a mudança de status (REQ-13, R-01), para os logs e para o
/// <c>pedido.cancelado</c> (REQ-15, R-01).
/// </summary>
public readonly record struct ResultadoCancelamento(bool EstoqueDevolvido, bool EstornoSolicitado, bool OrdemCancelada);
