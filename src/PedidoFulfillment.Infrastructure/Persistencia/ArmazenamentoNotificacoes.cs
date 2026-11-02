using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Notificacoes;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary><see cref="IArmazenamentoNotificacoes"/> sobre o <see cref="ContextoBancoDados"/> (REQ-18, research R-15).</summary>
internal sealed class ArmazenamentoNotificacoes(ContextoBancoDados contexto) : IArmazenamentoNotificacoes
{
    public async Task<IReadOnlyList<DestinoNotificacao>> ListarDestinosAsync(Guid? clienteId, CancellationToken cancellationToken) =>
        await contexto.DestinosNotificacao.AsNoTracking()
            .Where(d => d.ClienteId == clienteId)
            .OrderBy(d => d.CriadoEm)
            .ToListAsync(cancellationToken);

    public Task<DestinoNotificacao?> ObterDestinoAsync(Guid id, Guid? clienteId, CancellationToken cancellationToken) =>
        contexto.DestinosNotificacao.SingleOrDefaultAsync(d => d.Id == id && d.ClienteId == clienteId, cancellationToken);

    /// <summary>
    /// Transação com <c>sp_getapplock</c> do dono (o Cliente ou "global"): dois cadastros do mesmo
    /// dono esperam um pelo outro, e o limite não é ultrapassado (FR-010).
    /// </summary>
    public async Task<T> ExecutarComBloqueioDoDonoAsync<T>(Guid? clienteId, Func<CancellationToken, Task<T>> acao, CancellationToken cancellationToken)
    {
        await using var transacao = await contexto.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var recurso = $"oms-destinos:{clienteId?.ToString() ?? "global"}";
        await contexto.Database.ExecuteSqlAsync(
            $"EXEC sp_getapplock @Resource = {recurso}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000",
            cancellationToken);
        var resultado = await acao(cancellationToken);
        await transacao.CommitAsync(cancellationToken);
        return resultado;
    }

    public Task<int> ContarDestinosAsync(Guid? clienteId, CancellationToken cancellationToken) =>
        contexto.DestinosNotificacao.CountAsync(d => d.ClienteId == clienteId, cancellationToken);

    public void Adicionar(DestinoNotificacao destino) => contexto.DestinosNotificacao.Add(destino);

    public void Remover(DestinoNotificacao destino) => contexto.DestinosNotificacao.Remove(destino);

    public Task<int> DescartarPendentesAsync(Guid destinoId, DateTimeOffset instante, CancellationToken cancellationToken) =>
        contexto.EntregasNotificacao
            .Where(e => e.DestinoId == destinoId && e.Situacao == SituacaoEntrega.Pendente)
            .ExecuteUpdateAsync(s => s
                .SetProperty(e => e.Situacao, SituacaoEntrega.Descartada)
                .SetProperty(e => e.FinalizadaEm, instante), cancellationToken);

    public async Task<IReadOnlyList<ResumoEntrega>> ListarEntregasAsync(Guid destinoId, int limite, CancellationToken cancellationToken) =>
        await (
                from entrega in contexto.EntregasNotificacao.AsNoTracking()
                join aviso in contexto.AvisosPedido.AsNoTracking() on entrega.AvisoId equals aviso.Id
                where entrega.DestinoId == destinoId
                orderby entrega.Id descending
                select new ResumoEntrega(
                    aviso.IdNotificacao, aviso.Tipo, aviso.PedidoId, entrega.Situacao, entrega.Tentativas, entrega.UltimoCodigoHttp,
                    entrega.UltimoErro, entrega.CriadaEm, entrega.ProximaTentativaEm, entrega.FinalizadaEm))
            .Take(limite)
            .ToListAsync(cancellationToken);

    public Task SalvarAsync(CancellationToken cancellationToken) => contexto.SaveChangesAsync(cancellationToken);
}
