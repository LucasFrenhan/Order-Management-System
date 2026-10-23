using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Consumidor de pagamento (REQ-09, <c>specs/009-pagamento/contracts/consumidor-pagamento.md</c>):
/// cobra o pedido com estoque reservado e, conforme o gateway, aprova o pagamento ou o recusa
/// e devolve o estoque. Roda dentro da transação da base de consumo do REQ-06; o resultado sai
/// pelo outbox na mesma gravação.
/// </summary>
public sealed class ProcessamentoPagamento(
    IArmazenamentoPedidos armazenamento, IGatewayPagamento gateway, TimeProvider relogio, ILogger<ProcessamentoPagamento> logger)
    : IManipuladorEvento<EstoqueReservado>
{
    public string Consumidor => "pagamento";

    public IReadOnlyCollection<string> Tipos { get; } = [TiposEvento.EstoqueReservado];

    public async Task ManipularAsync(EnvelopeEvento<EstoqueReservado> evento, CancellationToken cancellationToken)
    {
        var pedidoId = evento.Dados.PedidoId;
        var pedido = await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, cancellationToken)
            ?? throw new InvalidOperationException($"O pedido {pedidoId} não existe.");
        var resultado = await gateway.CobrarAsync(pedido.Id, pedido.ValorTotal, cancellationToken);
        var agora = relogio.GetUtcNow();

        if (resultado.Aprovado)
        {
            await AprovarAsync(evento.IdEvento, pedido, resultado.IdentificadorTransacao!, agora, cancellationToken);
        }
        else
        {
            await RecusarAsync(evento.IdEvento, pedido, resultado.Motivo!, agora, cancellationToken);
        }
    }

    /// <summary>Pagamento aprovado, Pendente → Processando e <c>pagamento.aprovado</c> (FR-008).</summary>
    private async Task AprovarAsync(Guid idEvento, Pedido pedido, string identificadorTransacao, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        pedido.AprovarPagamento(identificadorTransacao, agora);
        var pagamento = pedido.Pagamento!;
        armazenamento.Adicionar(EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new PagamentoAprovado
        {
            PedidoId = pedido.Id,
            Valor = pagamento.Valor,
            IdentificadorTransacao = identificadorTransacao
        }, agora), agora));
        await armazenamento.SalvarAsync(cancellationToken);
        EventosPagamento.PagamentoAprovado(logger, idEvento, pedido.Id, pagamento.Valor, identificadorTransacao);
    }

    /// <summary>
    /// Pagamento recusado, estoque devolvido, PagamentoRejeitado e <c>pagamento.recusado</c>, na
    /// mesma gravação (FR-009, clarify). Os produtos só são lidos aqui: a aprovação não mexe em estoque (R-06).
    /// </summary>
    private async Task RecusarAsync(Guid idEvento, Pedido pedido, string motivo, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var ids = pedido.Itens.Select(i => i.ProdutoId).ToList();
        var produtos = (await armazenamento.ObterProdutosAsync(ids, paraAlterar: true, cancellationToken)).ToDictionary(p => p.Id);
        pedido.RecusarPagamento(motivo, produtos, agora);
        armazenamento.Adicionar(EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new PagamentoRecusado
        {
            PedidoId = pedido.Id,
            Valor = pedido.Pagamento!.Valor,
            Motivo = motivo
        }, agora), agora));
        await armazenamento.SalvarAsync(cancellationToken);
        EventosPagamento.PagamentoRecusado(logger, idEvento, pedido.Id, pedido.Pagamento.Valor);
    }
}
