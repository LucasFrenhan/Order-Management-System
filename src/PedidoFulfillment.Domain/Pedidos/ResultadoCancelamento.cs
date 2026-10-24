namespace PedidoFulfillment.Domain.Pedidos;

/// <summary>O que o cancelamento fez junto com a mudança de status (REQ-13, R-01), para os logs.</summary>
public readonly record struct ResultadoCancelamento(bool EstoqueDevolvido, bool EstornoSolicitado, bool OrdemCancelada);
