using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Mensageria;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>Pedidos de reenvio da DLQ (REQ-14, R-03 e R-07), usados pela API e pelo Worker.</summary>
internal sealed class ArmazenamentoReenvios(ContextoBancoDados contexto) : IArmazenamentoReenvios
{
    public void Adicionar(PedidoReenvioDlq pedido) => contexto.PedidosReenvioDlq.Add(pedido);

    public Task<PedidoReenvioDlq?> ObterAsync(Guid id, CancellationToken cancellationToken) =>
        contexto.PedidosReenvioDlq.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<PedidoReenvioDlq>> ListarParaExecucaoAsync(int quantidade, CancellationToken cancellationToken) =>
        await contexto.PedidosReenvioDlq
            .Where(p => p.Situacao == SituacaoReenvioDlq.EmExecucao || p.Situacao == SituacaoReenvioDlq.Pendente)
            .OrderBy(p => p.Situacao == SituacaoReenvioDlq.EmExecucao ? 0 : 1)
            .ThenBy(p => p.RegistradoEm)
            .Take(quantidade)
            .ToListAsync(cancellationToken);

    public Task SalvarAsync(CancellationToken cancellationToken) => contexto.SaveChangesAsync(cancellationToken);
}
