using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// <see cref="IArmazenamentoPedidos"/> sobre o <see cref="ContextoBancoDados"/> (research R-02 do
/// REQ-05). O conflito de concorrência já chega traduzido pelo contexto.
/// </summary>
internal sealed class ArmazenamentoPedidos(ContextoBancoDados contexto) : IArmazenamentoPedidos
{
    public async Task<IReadOnlyList<Produto>> ObterProdutosAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await contexto.Produtos.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);

    public Task<Pedido?> ObterPedidoAsync(Guid id, bool paraAlterar, CancellationToken cancellationToken)
    {
        var pedidos = paraAlterar ? contexto.Pedidos : contexto.Pedidos.AsNoTracking();
        return pedidos.Include(p => p.Itens).SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public Task<RegistroIdempotencia?> ObterRegistroIdempotenciaAsync(Guid clienteId, Guid chave, CancellationToken cancellationToken) =>
        contexto.ChavesIdempotencia.AsNoTracking().SingleOrDefaultAsync(r => r.ClienteId == clienteId && r.Chave == chave, cancellationToken);

    public void Adicionar(Pedido pedido, RegistroIdempotencia registro, EventoSaida evento)
    {
        contexto.Pedidos.Add(pedido);
        contexto.ChavesIdempotencia.Add(registro);
        contexto.EventosSaida.Add(evento);
    }

    public async Task SalvarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await contexto.SaveChangesAsync(cancellationToken);
        }
        // A outra requisição com a mesma chave venceu; um deadlock entre elas tem o mesmo
        // tratamento: reler o registro (research R-04).
        catch (DbUpdateException excecao) when (
            ErrosBancoDados.EhViolacaoUnicidade(excecao, ContextoBancoDados.NomeChavePrimariaIdempotencia) || ErrosBancoDados.EhDeadlock(excecao))
        {
            throw new ChaveIdempotenciaRegistradaException(excecao);
        }
    }

    public void DescartarAlteracoes() => contexto.ChangeTracker.Clear();
}
