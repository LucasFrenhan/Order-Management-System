using PedidoFulfillment.Domain.Comum;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Domain.Pedidos;

/// <summary>
/// Linha do pedido. Guarda uma cópia do nome e do preço do produto no momento da compra
/// (FR-008); não muda depois de criada.
/// </summary>
public sealed class ItemPedido
{
    internal ItemPedido(Guid pedidoId, Produto produto, int quantidade)
    {
        ArgumentNullException.ThrowIfNull(produto);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantidade);
        PedidoId = pedidoId;
        ProdutoId = produto.Id;
        NomeProduto = produto.Nome;
        Quantidade = quantidade;
        PrecoUnitario = produto.Preco;
        Subtotal = Guardas.Dinheiro(produto.Preco * quantidade);
    }

    private ItemPedido()
    {
        NomeProduto = "";
    }

    public Guid PedidoId { get; private set; }

    public Guid ProdutoId { get; private set; }

    public string NomeProduto { get; private set; }

    public int Quantidade { get; private set; }

    public decimal PrecoUnitario { get; private set; }

    public decimal Subtotal { get; private set; }
}
