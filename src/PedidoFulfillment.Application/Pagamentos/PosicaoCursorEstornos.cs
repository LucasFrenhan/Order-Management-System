using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Posição na ordem da listagem dos estornos pendentes, <c>EstornoSolicitadoEm ASC, Id ASC</c>
/// (REQ-28, R-08), e o sentido da navegação; <see cref="Inclusiva"/> só no cursor de volta de uma
/// página vazia, como em usuários e produtos.
/// </summary>
public sealed record PosicaoCursorEstornos(SentidoCursor Sentido, DateTimeOffset SolicitadoEm, Guid Id, bool Inclusiva = false);
