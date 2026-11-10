using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Pagamentos;

/// <summary>
/// Estornos pendentes da rodada, em duas etapas (REQ-28, R-04): primeiro os com tentativa forçada pela
/// operação, do pedido mais antigo para o mais novo (<c>IX_Pagamentos_EstornoForcado</c>); depois o
/// lote se completa com a fila de sempre (REQ-13, R-04), pelo índice filtrado
/// <c>IX_Pagamentos_EstornoPendente</c>: no SQL Server os nulos vêm primeiro, então os que nunca
/// falharam saem antes, do mais antigo para o mais novo, e depois o tentado há mais tempo.
/// </summary>
internal sealed class ConsultaEstornosPendentes(ContextoBancoDados contexto) : IConsultaEstornosPendentes
{
    public async Task<IReadOnlyList<EstornoPendente>> ListarAsync(int quantidade, CancellationToken cancellationToken)
    {
        var pendentes = contexto.Set<Pagamento>().AsNoTracking().Where(p => p.Situacao == SituacaoPagamento.EstornoPendente);

        var forcados = await Projetar(pendentes
                .Where(p => p.TentativaEstornoForcadaEm != null)
                .OrderBy(p => p.TentativaEstornoForcadaEm)
                .Take(quantidade))
            .ToListAsync(cancellationToken);
        if (forcados.Count == quantidade)
        {
            return forcados;
        }

        var demais = await Projetar(pendentes
                .Where(p => p.TentativaEstornoForcadaEm == null)
                .OrderBy(p => p.UltimaFalhaEstornoEm)
                .ThenBy(p => p.EstornoSolicitadoEm)
                .Take(quantidade - forcados.Count))
            .ToListAsync(cancellationToken);
        return [.. forcados, .. demais];
    }

    /// <summary>O contexto da criação do pedido, para o passo da rodada se ligar a ele (REQ-16, R-05).</summary>
    private IQueryable<EstornoPendente> Projetar(IQueryable<Pagamento> pagamentos) =>
        pagamentos.Select(p => new EstornoPendente(
            p.PedidoId,
            contexto.Pedidos.Where(pedido => pedido.Id == p.PedidoId)
                .Select(pedido => EF.Property<string?>(pedido, ContextoBancoDados.ContextoRastreio))
                .FirstOrDefault()));
}
