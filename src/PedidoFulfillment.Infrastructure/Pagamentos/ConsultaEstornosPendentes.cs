using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Pagamentos;

/// <summary>
/// Estornos pendentes da rodada (REQ-13, R-04), pelo índice filtrado
/// <c>IX_Pagamentos_EstornoPendente</c>: no SQL Server os nulos vêm primeiro, então os que nunca
/// falharam saem antes, do mais antigo para o mais novo, e depois o tentado há mais tempo.
/// </summary>
internal sealed class ConsultaEstornosPendentes(ContextoBancoDados contexto) : IConsultaEstornosPendentes
{
    public async Task<IReadOnlyList<EstornoPendente>> ListarAsync(int quantidade, CancellationToken cancellationToken) =>
        await contexto.Set<Pagamento>().AsNoTracking()
            .Where(p => p.Situacao == SituacaoPagamento.EstornoPendente)
            .OrderBy(p => p.UltimaFalhaEstornoEm)
            .ThenBy(p => p.EstornoSolicitadoEm)
            .Take(quantidade)
            // O contexto da criação do pedido, para o passo da rodada se ligar a ele (REQ-16, R-05).
            .Select(p => new EstornoPendente(
                p.PedidoId,
                contexto.Pedidos.Where(pedido => pedido.Id == p.PedidoId)
                    .Select(pedido => EF.Property<string?>(pedido, ContextoBancoDados.ContextoRastreio))
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);
}
