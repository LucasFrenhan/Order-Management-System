using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Pagamentos;

/// <summary>
/// Leitura dos estornos para a operação (REQ-28, R-08). A listagem usa o índice filtrado
/// <c>IX_Pagamentos_EstornoPendenteListagem</c> (<c>EstornoSolicitadoEm, Id</c>); a consulta por id só
/// mostra pagamentos que tiveram estorno (pendente, confirmado ou baixado).
/// </summary>
internal sealed class ConsultaEstornos(ContextoBancoDados contexto) : IConsultaEstornos
{
    private static readonly SituacaoPagamento[] ComEstorno =
        [SituacaoPagamento.EstornoPendente, SituacaoPagamento.Estornado, SituacaoPagamento.EstornoBaixado];

    public async Task<IReadOnlyList<EstornoLido>> ListarPendentesAsync(
        PosicaoCursorEstornos? posicao, int quantidade, CancellationToken cancellationToken)
    {
        var pendentes = contexto.Pagamentos.AsNoTracking().Where(p => p.Situacao == SituacaoPagamento.EstornoPendente);

        // A ordem é crescente (EstornoSolicitadoEm, Id): "depois" é maior e "antes" é menor.
        if (posicao is { Sentido: SentidoCursor.Anterior } anterior)
        {
            var (s, i) = (anterior.SolicitadoEm, anterior.Id);
            pendentes = anterior.Inclusiva
                ? pendentes.Where(p => p.EstornoSolicitadoEm < s || (p.EstornoSolicitadoEm == s && p.Id.CompareTo(i) <= 0))
                : pendentes.Where(p => p.EstornoSolicitadoEm < s || (p.EstornoSolicitadoEm == s && p.Id.CompareTo(i) < 0));
            var lidos = await Projetar(pendentes.OrderByDescending(p => p.EstornoSolicitadoEm).ThenByDescending(p => p.Id).Take(quantidade))
                .ToListAsync(cancellationToken);
            lidos.Reverse();
            return lidos;
        }

        if (posicao is { } proxima)
        {
            var (s, i) = (proxima.SolicitadoEm, proxima.Id);
            pendentes = proxima.Inclusiva
                ? pendentes.Where(p => p.EstornoSolicitadoEm > s || (p.EstornoSolicitadoEm == s && p.Id.CompareTo(i) >= 0))
                : pendentes.Where(p => p.EstornoSolicitadoEm > s || (p.EstornoSolicitadoEm == s && p.Id.CompareTo(i) > 0));
        }

        return await Projetar(pendentes.OrderBy(p => p.EstornoSolicitadoEm).ThenBy(p => p.Id).Take(quantidade))
            .ToListAsync(cancellationToken);
    }

    public Task<EstornoLido?> ObterAsync(Guid estornoId, CancellationToken cancellationToken) =>
        Projetar(contexto.Pagamentos.AsNoTracking().Where(p => p.Id == estornoId && ComEstorno.Contains(p.Situacao)))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>A projeção vem por último, para a ordem da consulta valer no resultado.</summary>
    private IQueryable<EstornoLido> Projetar(IQueryable<Pagamento> pagamentos) =>
        pagamentos.Select(p => new EstornoLido(
            p.Id,
            p.PedidoId,
            contexto.Pedidos.Where(pedido => pedido.Id == p.PedidoId).Select(pedido => pedido.Numero).First(),
            p.Valor,
            p.Situacao,
            p.EstornoSolicitadoEm!.Value,
            p.TentativasEstorno,
            p.UltimaFalhaEstornoEm,
            p.UltimoErroEstorno,
            p.EstornoAtrasadoEm,
            p.TentativaEstornoForcadaEm,
            p.EstornadoEm,
            p.EstornoBaixadoEm,
            p.EstornoBaixadoPorId,
            p.MotivoBaixaEstorno,
            p.ReferenciaExternaBaixaEstorno));
}
