using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.Application.Usuarios;

/// <summary>
/// Posição na ordem da listagem de usuários, <c>Nome ASC, Id ASC</c> (REQ-25, R-07), e o sentido da
/// navegação; <see cref="Inclusiva"/> só no cursor de volta de uma página vazia, como em produtos.
/// </summary>
public sealed record PosicaoCursorUsuarios(SentidoCursor Sentido, string Nome, Guid Id, bool Inclusiva = false);
