using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Estoque;

/// <summary>
/// Consumidor de estoque (REQ-08, <c>specs/008-estoque/contracts/consumidor-estoque.md</c>):
/// reserva o estoque do pedido criado ou, sem estoque, cancela o pedido. Roda dentro da
/// transação da base de consumo do REQ-06; o resultado sai pelo outbox na mesma gravação.
/// </summary>
public sealed class ReservaEstoque(IArmazenamentoPedidos armazenamento, TimeProvider relogio, ILogger<ReservaEstoque> logger)
    : IManipuladorEvento<PedidoCriado>
{
    /// <summary>Motivo do histórico no cancelamento por falta de estoque (FR-006).</summary>
    public const string MotivoSemEstoque = "Estoque insuficiente.";

    /// <summary>Como <c>TentativasStatus</c> do <see cref="ServicoPedidos"/> (research R-03).</summary>
    private const int Reavaliacoes = 3;

    public string Consumidor => "estoque";

    public IReadOnlyCollection<string> Tipos { get; } = [TiposEvento.PedidoCriado];

    /// <summary>
    /// Ler, decidir e gravar, até <see cref="Reavaliacoes"/> vezes em conflito de concorrência
    /// (FR-008). Dentro da transação da base, o savepoint do EF Core desfaz só a tentativa que
    /// perdeu; esgotadas as reavaliações, o conflito vai para as novas tentativas da base (FR-009).
    /// </summary>
    public async Task ManipularAsync(EnvelopeEvento<PedidoCriado> evento, CancellationToken cancellationToken)
    {
        for (var reavaliacao = 1; ; reavaliacao++)
        {
            try
            {
                await ReservarAsync(evento, cancellationToken);
                return;
            }
            catch (ConflitoConcorrenciaException)
            {
                armazenamento.DescartarAlteracoes();
                EventosEstoque.ConflitoNaReserva(logger, evento.IdEvento, evento.Dados.PedidoId, reavaliacao);
                if (reavaliacao == Reavaliacoes)
                {
                    throw;
                }
            }
        }
    }

    /// <summary>Os itens vêm do pedido gravado; o evento só identifica o pedido (FR-003).</summary>
    private async Task ReservarAsync(EnvelopeEvento<PedidoCriado> evento, CancellationToken cancellationToken)
    {
        var pedidoId = evento.Dados.PedidoId;
        var pedido = await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, cancellationToken)
            ?? throw new InvalidOperationException($"O pedido {pedidoId} não existe.");
        var ids = pedido.Itens.Select(i => i.ProdutoId).ToList();
        var produtos = (await armazenamento.ObterProdutosAsync(ids, paraAlterar: true, cancellationToken)).ToDictionary(p => p.Id);
        var agora = relogio.GetUtcNow();

        var faltas = pedido.ReservarEstoque(produtos, agora);
        if (faltas.Count > 0)
        {
            await CancelarSemEstoqueAsync(evento.IdEvento, pedido, faltas, agora, cancellationToken);
            return;
        }

        armazenamento.Adicionar(EventoSaida.De(EventoEstoqueReservado(pedido, agora), agora));
        await armazenamento.SalvarAsync(cancellationToken);
        EventosEstoque.EstoqueReservado(logger, evento.IdEvento, pedido.Id, pedido.Itens.Count);
    }

    /// <summary>
    /// Nenhum estoque mudou (tudo ou nada); o pedido vai para Cancelado, sem autor, e as faltas
    /// saem em <c>estoque.insuficiente</c> (FR-006).
    /// </summary>
    private async Task CancelarSemEstoqueAsync(Guid idEvento, Pedido pedido, IReadOnlyList<FaltaEstoque> faltas, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        if (!TransicoesStatusPedido.PermitidaParaOperacao(pedido.Status, StatusPedido.Cancelado))
        {
            throw new InvalidOperationException($"O pedido {pedido.Id} está em {pedido.Status} e não pode ser cancelado.");
        }

        pedido.MudarStatus(StatusPedido.Cancelado, null, MotivoSemEstoque, agora);
        armazenamento.Adicionar(EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new EstoqueInsuficiente
        {
            PedidoId = pedido.Id,
            Itens = [.. faltas.OrderBy(f => f.ProdutoId).Select(f => new ItemEstoqueInsuficiente
            {
                ProdutoId = f.ProdutoId,
                QuantidadeSolicitada = f.QuantidadeSolicitada,
                QuantidadeDisponivel = f.QuantidadeDisponivel,
                Inativo = f.Inativo
            })]
        }, agora), agora));
        await armazenamento.SalvarAsync(cancellationToken);
        EventosEstoque.PedidoCanceladoSemEstoque(logger, idEvento, pedido.Id, faltas.Count);
    }

    private static EnvelopeEvento<EstoqueReservado> EventoEstoqueReservado(Pedido pedido, DateTimeOffset agora) =>
        EnvelopeEvento.Criar(pedido.Id, new EstoqueReservado
        {
            PedidoId = pedido.Id,
            Itens = [.. pedido.Itens.OrderBy(i => i.ProdutoId).Select(i => new ItemEstoqueReservado { ProdutoId = i.ProdutoId, Quantidade = i.Quantidade })]
        }, agora);
}
