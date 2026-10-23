using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Entregas;

/// <summary>
/// Consumidor de entrega (REQ-10, <c>specs/010-fulfillment/contracts/consumidor-entrega.md</c>):
/// cria a ordem de entrega do pedido pago, leva o pedido a ProntoParaEnvio e publica
/// <c>entrega.criada</c>. Roda dentro da transação da base de consumo do REQ-06; o evento sai pelo
/// outbox na mesma gravação.
/// </summary>
public sealed class CriacaoOrdemEntrega(IArmazenamentoPedidos armazenamento, TimeProvider relogio, ILogger<CriacaoOrdemEntrega> logger)
    : IManipuladorEvento<PagamentoAprovado>
{
    public string Consumidor => "entrega";

    public IReadOnlyCollection<string> Tipos { get; } = [TiposEvento.PagamentoAprovado];

    public async Task ManipularAsync(EnvelopeEvento<PagamentoAprovado> evento, CancellationToken cancellationToken)
    {
        var pedidoId = evento.Dados.PedidoId;
        var pedido = await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, cancellationToken)
            ?? throw new InvalidOperationException($"O pedido {pedidoId} não existe.");
        await CriarAsync(evento.IdEvento, pedido, cancellationToken);
    }

    /// <summary>Ordem, status e <c>entrega.criada</c> na mesma gravação (FR-004); os itens vêm do pedido gravado.</summary>
    private async Task CriarAsync(Guid idEvento, Pedido pedido, CancellationToken cancellationToken)
    {
        var agora = relogio.GetUtcNow();
        var statusAntes = pedido.Status;
        var ordem = pedido.CriarOrdemEntrega(agora);
        armazenamento.Adicionar(EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new EntregaCriada
        {
            PedidoId = pedido.Id,
            Numero = pedido.Numero,
            OrdemEntregaId = ordem.Id,
            Itens = [.. pedido.Itens.Select(i => new ItemEntregaCriada { ProdutoId = i.ProdutoId, Quantidade = i.Quantidade })]
        }, agora), agora));
        await armazenamento.SalvarAsync(cancellationToken);
        EventosEntrega.OrdemEntregaCriada(logger, idEvento, pedido.Id, ordem.Id, pedido.Status != statusAntes);
    }
}
