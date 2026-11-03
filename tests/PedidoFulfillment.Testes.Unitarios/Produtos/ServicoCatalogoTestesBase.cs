using PedidoFulfillment.Application.Produtos;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;
using PedidoFulfillment.Testes.Unitarios.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Produtos;

/// <summary>Montagem comum dos testes do <see cref="ServicoCatalogo"/> (REQ-22).</summary>
public abstract class ServicoCatalogoTestesBase
{
    protected static readonly Guid Admin = Guid.Parse("0199f000-0000-7000-8000-00000000000a");
    protected static readonly DateTimeOffset Agora = new(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);

    protected ArmazenamentoProdutosFalso Armazenamento { get; } = new();

    protected LoggerFalso<ServicoCatalogo> Logger { get; } = new();

    protected ServicoCatalogo Servico() => new(
        Armazenamento,
        new CodificadorCursorProdutos(new CodificadorCursorPedidosTestes.ChaveFixa("chave-de-teste-com-mais-de-32-bytes!")),
        new RelogioFixo(Agora),
        Logger);

    protected IEnumerable<int> IdsDeLog() => Logger.Eventos.Select(e => e.Id.Id);
}
