namespace PedidoFulfillment.Domain.Pedidos;

/// <summary>
/// Item que impediu a reserva (REQ-08, data-model): produto inativo ou com estoque menor que a
/// quantidade pedida. Não é persistido; vira o conteúdo de <c>estoque.insuficiente</c>.
/// </summary>
public sealed record FaltaEstoque(Guid ProdutoId, int QuantidadeSolicitada, int QuantidadeDisponivel, bool Inativo);
