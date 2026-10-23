using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;

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

        pedido.AprovarPagamento(resultado.IdentificadorTransacao!, agora);
        armazenamento.Adicionar(EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new PagamentoAprovado
        {
            PedidoId = pedido.Id,
            Valor = pedido.Pagamento!.Valor,
            IdentificadorTransacao = pedido.Pagamento.IdentificadorTransacao!
        }, agora), agora));
        await armazenamento.SalvarAsync(cancellationToken);
        EventosPagamento.PagamentoAprovado(logger, evento.IdEvento, pedido.Id, pedido.Pagamento.Valor, pedido.Pagamento.IdentificadorTransacao!);
    }
}
