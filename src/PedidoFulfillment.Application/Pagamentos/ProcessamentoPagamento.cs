using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Application.Reconciliacao;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Consumidor de pagamento (REQ-09, <c>specs/009-pagamento/contracts/consumidor-pagamento.md</c>):
/// cobra o pedido com estoque reservado e, conforme o gateway, aprova o pagamento ou o recusa
/// e devolve o estoque. Roda dentro da transação da base de consumo do REQ-06; o resultado sai
/// pelo outbox na mesma gravação. A reconciliação aplica a mesma regra ao pedido parado sem
/// cobrança (REQ-12, R-04).
/// </summary>
public sealed class ProcessamentoPagamento(
    IArmazenamentoPedidos armazenamento, IGatewayPagamento gateway, TimeProvider relogio, ILogger<ProcessamentoPagamento> logger)
    : IManipuladorEvento<EstoqueReservado>, IEtapaFluxoPedido
{
    /// <summary>Como <see cref="Estoque.ReservaEstoque"/> (research R-05).</summary>
    private const int Reavaliacoes = 3;

    public string Consumidor => "pagamento";

    public IReadOnlyCollection<string> Tipos { get; } = [TiposEvento.EstoqueReservado];

    public EtapaFluxoPedido Etapa => EtapaFluxoPedido.Pagamento;

    public Task ManipularAsync(EnvelopeEvento<EstoqueReservado> evento, CancellationToken cancellationToken) =>
        AplicarAsync(evento.IdEvento, evento.Dados.PedidoId, cancellationToken);

    /// <summary>
    /// Ler, decidir e gravar, até <see cref="Reavaliacoes"/> vezes em conflito de concorrência
    /// (FR-012). O gateway é chamado uma vez só, na primeira leitura em que o pedido está apto, e
    /// o resultado vale para as reavaliações: uma mesma mensagem não sorteia duas vezes (R-05).
    /// Esgotadas as reavaliações, o conflito vai para as novas tentativas da base.
    /// </summary>
    public async Task<bool> AplicarAsync(Guid idOrigem, Guid pedidoId, CancellationToken cancellationToken)
    {
        ResultadoCobranca? resultado = null;
        for (var reavaliacao = 1; ; reavaliacao++)
        {
            try
            {
                if (await LerAptoAsync(idOrigem, pedidoId, cancellationToken) is not { } pedido)
                {
                    return false;
                }

                resultado ??= await gateway.CobrarAsync(pedido.Id, pedido.ValorTotal, cancellationToken);
                await GravarResultadoAsync(idOrigem, pedido, resultado, cancellationToken);
                return true;
            }
            catch (ConflitoConcorrenciaException)
            {
                armazenamento.DescartarAlteracoes();
                EventosPagamento.ConflitoNoPagamento(logger, idOrigem, pedidoId, reavaliacao);
                if (reavaliacao == Reavaliacoes)
                {
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// O pedido atual, se ainda pode ser cobrado; os itens e o valor vêm do pedido gravado, não do
    /// evento (FR-002, FR-006).
    /// </summary>
    private async Task<Pedido?> LerAptoAsync(Guid idOrigem, Guid pedidoId, CancellationToken cancellationToken)
    {
        var pedido = await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, cancellationToken)
            ?? throw new InvalidOperationException($"O pedido {pedidoId} não existe.");
        if (pedido.AptoParaPagamento)
        {
            return pedido;
        }

        // Cancelado antes da cobrança, em status final ou já pago por outro evento: nada a fazer (FR-002).
        EventosPagamento.PedidoIgnoradoNoPagamento(logger, idOrigem, pedido.Id, pedido.Status, pedido.EstoqueReservado, pedido.Pagamento is not null);
        return null;
    }

    private async Task GravarResultadoAsync(Guid idOrigem, Pedido pedido, ResultadoCobranca resultado, CancellationToken cancellationToken)
    {
        var agora = relogio.GetUtcNow();
        if (resultado.Aprovado)
        {
            await AprovarAsync(idOrigem, pedido, resultado.IdentificadorTransacao!, agora, cancellationToken);
        }
        else
        {
            await RecusarAsync(idOrigem, pedido, resultado.Motivo!, agora, cancellationToken);
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
