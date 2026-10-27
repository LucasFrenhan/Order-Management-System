using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Reconciliacao;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Reconciliacao;

/// <summary>
/// Pedidos parados por etapa (REQ-12, R-05). Os predicados espelham o domínio: um teste de
/// integração compara a seleção com <c>AptoParaPagamento</c> e <c>AptoParaOrdemEntrega</c>.
/// </summary>
internal sealed class ConsultaPedidosParados(ContextoBancoDados contexto) : IConsultaPedidosParados
{
    public async Task<IReadOnlyList<PedidoParado>> ListarAsync(
        DateTimeOffset paradoAntesDe, int quantidade, IReadOnlyCollection<Guid> comFalha, CancellationToken cancellationToken)
    {
        var ids = comFalha.ToList();

        // Primeiro os pedidos sem falha anterior, depois os que já falharam (FR-011, SC-007).
        var lote = await Candidatos(paradoAntesDe)
            .Where(c => !ids.Contains(c.PedidoId))
            .OrderBy(c => c.AtualizadoEm)
            .Take(quantidade)
            .ToListAsync(cancellationToken);
        if (lote.Count < quantidade && ids.Count > 0)
        {
            lote.AddRange(await Candidatos(paradoAntesDe)
                .Where(c => ids.Contains(c.PedidoId))
                .OrderBy(c => c.AtualizadoEm)
                .Take(quantidade - lote.Count)
                .ToListAsync(cancellationToken));
        }

        return [.. lote.Select(c => new PedidoParado(c.PedidoId, (EtapaFluxoPedido)c.Etapa, c.AtualizadoEm, c.ContextoRastreio))];
    }

    /// <summary>A etapa sai de uma expressão condicional, na ordem do fluxo; zero é "nenhuma".</summary>
    private IQueryable<Candidato> Candidatos(DateTimeOffset paradoAntesDe) =>
        contexto.Pedidos.AsNoTracking()
            .Where(p => p.AtualizadoEm < paradoAntesDe)
            .Select(p => new Candidato
            {
                PedidoId = p.Id,
                AtualizadoEm = p.AtualizadoEm,
                // Contexto da criação, para o passo da rodada se ligar ao pedido (REQ-16, R-05).
                ContextoRastreio = EF.Property<string?>(p, ContextoBancoDados.ContextoRastreio),
                Etapa =
                    // Estoque: status não final, sem reserva (REQ-08).
                    !p.EstoqueReservado
                    && (p.Status == StatusPedido.Pendente || p.Status == StatusPedido.Processando || p.Status == StatusPedido.ProntoParaEnvio)
                        ? (int)EtapaFluxoPedido.Estoque
                    // Pagamento: AptoParaPagamento (REQ-09).
                    : p.EstoqueReservado && p.Pagamento == null
                    && (p.Status == StatusPedido.Pendente || p.Status == StatusPedido.Processando || p.Status == StatusPedido.ProntoParaEnvio)
                        ? (int)EtapaFluxoPedido.Pagamento
                    // Entrega: AptoParaOrdemEntrega (REQ-10).
                    : p.EstoqueReservado && p.Pagamento != null && p.Pagamento.Situacao == SituacaoPagamento.Aprovado
                    && p.OrdemEntrega == null && (p.Status == StatusPedido.Processando || p.Status == StatusPedido.ProntoParaEnvio)
                        ? (int)EtapaFluxoPedido.Entrega
                        : 0
            })
            .Where(c => c.Etapa != 0);

    private sealed class Candidato
    {
        public Guid PedidoId { get; init; }

        public DateTimeOffset AtualizadoEm { get; init; }

        public int Etapa { get; init; }

        public string? ContextoRastreio { get; init; }
    }
}
