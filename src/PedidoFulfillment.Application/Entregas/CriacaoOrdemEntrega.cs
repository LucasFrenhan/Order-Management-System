using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Application.Reconciliacao;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Entregas;

/// <summary>
/// Consumidor de entrega (REQ-10, <c>specs/010-fulfillment/contracts/consumidor-entrega.md</c>):
/// cria a ordem de entrega do pedido pago, leva o pedido a ProntoParaEnvio e publica
/// <c>entrega.criada</c>. Roda dentro da transação da base de consumo do REQ-06; o evento sai pelo
/// outbox na mesma gravação. A reconciliação aplica a mesma regra ao pedido pago sem ordem
/// (REQ-12, R-04).
/// </summary>
public sealed class CriacaoOrdemEntrega(IArmazenamentoPedidos armazenamento, TimeProvider relogio, ILogger<CriacaoOrdemEntrega> logger)
    : IManipuladorEvento<PagamentoAprovado>, IEtapaFluxoPedido
{
    /// <summary>Como <see cref="Pagamentos.ProcessamentoPagamento"/> (research R-04).</summary>
    private const int Reavaliacoes = 3;

    public string Consumidor => ConsumidoresConhecidos.Entrega;

    public IReadOnlyCollection<string> Tipos { get; } = [TiposEvento.PagamentoAprovado];

    public EtapaFluxoPedido Etapa => EtapaFluxoPedido.Entrega;

    public Task ManipularAsync(EnvelopeEvento<PagamentoAprovado> evento, CancellationToken cancellationToken) =>
        AplicarAsync(evento.IdEvento, evento.Dados.PedidoId, cancellationToken);

    /// <summary>
    /// Ler, decidir e gravar, até <see cref="Reavaliacoes"/> vezes em conflito de concorrência
    /// (FR-007). A cada volta o pedido é relido: se deixou de estar apto (por exemplo, foi
    /// cancelado), termina sem gravar. Esgotadas as reavaliações, o conflito vai para as novas
    /// tentativas da base.
    /// </summary>
    public async Task<bool> AplicarAsync(Guid idOrigem, Guid pedidoId, CancellationToken cancellationToken)
    {
        for (var reavaliacao = 1; ; reavaliacao++)
        {
            try
            {
                if (await LerAptoAsync(idOrigem, pedidoId, cancellationToken) is not { } pedido)
                {
                    return false;
                }

                await CriarAsync(idOrigem, pedido, cancellationToken);
                return true;
            }
            catch (ConflitoConcorrenciaException)
            {
                armazenamento.DescartarAlteracoes();
                EventosEntrega.ConflitoNaEntrega(logger, idOrigem, pedidoId, reavaliacao);
                if (reavaliacao == Reavaliacoes)
                {
                    throw;
                }
            }
        }
    }

    /// <summary>O pedido atual, se ainda pode ganhar a ordem (FR-002); inexistente é falha (FR-003).</summary>
    private async Task<Pedido?> LerAptoAsync(Guid idOrigem, Guid pedidoId, CancellationToken cancellationToken)
    {
        var pedido = await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, cancellationToken)
            ?? throw new InvalidOperationException($"O pedido {pedidoId} não existe.");
        if (pedido.AptoParaOrdemEntrega)
        {
            return pedido;
        }

        // Cancelado ou despachado antes do fulfillment, sem pagamento aprovado ou já com ordem: nada a fazer (FR-002).
        EventosEntrega.PedidoIgnoradoNaEntrega(logger, idOrigem, pedido.Id, pedido.Status, pedido.EstoqueReservado,
            pedido.Pagamento?.Situacao == SituacaoPagamento.Aprovado, pedido.OrdemEntrega is not null);
        return null;
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
