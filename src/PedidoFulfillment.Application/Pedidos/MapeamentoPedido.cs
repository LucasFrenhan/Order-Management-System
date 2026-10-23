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
        AtualizadoEm = pedido.AtualizadoEm,

        // Em ordem de ocorrência e, no empate, de gravação; sem o autor (REQ-11, FR-002 e FR-003).
        Historico = [.. pedido.Historico.OrderBy(h => h.OcorridoEm).ThenBy(h => h.Id).Select(h => new HistoricoStatusPedidoResponseDTO
        {
            StatusAnterior = h.StatusAnterior,
            StatusNovo = h.StatusNovo,
            OcorridoEm = h.OcorridoEm,
            Motivo = h.Motivo
        })],
        Entrega = pedido.OrdemEntrega is { } ordem
            ? new EntregaPedidoResponseDTO
            {
                Transportadora = ordem.Transportadora,
                CodigoRastreio = ordem.CodigoRastreio,
                DespachadoEm = ordem.DespachadoEm,
                CriadoEm = ordem.CriadoEm
            }
            : null
    };
}
