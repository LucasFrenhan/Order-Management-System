using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Pedidos;

internal static class MapeamentoPedido
{
    /// <summary>Itens em ordem de produto, para que a resposta seja estável entre leituras.</summary>
    public static PedidoResponseDTO ParaResposta(this Pedido pedido) => new()
    {
        Id = pedido.Id,
        Numero = pedido.Numero,
        ClienteId = pedido.ClienteId,
        Status = pedido.Status,
        Itens = [.. pedido.Itens.OrderBy(i => i.ProdutoId).Select(i => new ItemPedidoResponseDTO
        {
            ProdutoId = i.ProdutoId,
            NomeProduto = i.NomeProduto,
            Quantidade = i.Quantidade,
            PrecoUnitario = i.PrecoUnitario,
            Subtotal = i.Subtotal
        })],
        ValorTotal = pedido.ValorTotal,
        Moeda = pedido.Moeda,
        CriadoEm = pedido.CriadoEm,
        AtualizadoEm = pedido.AtualizadoEm
    };
}
