using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.Application.Produtos;

/// <summary>
/// Posição na ordem da listagem de produtos, <c>Nome ASC, Sku ASC</c> (REQ-22, research R-05), e
/// o sentido da navegação; <see cref="Inclusiva"/> só no cursor de volta de uma página vazia,
/// como na listagem de pedidos (REQ-21, research R-02).
/// </summary>
public sealed record PosicaoCursorProdutos(SentidoCursor Sentido, string Nome, string Sku, bool Inclusiva = false);
