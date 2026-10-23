using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// <see cref="IArmazenamentoPedidos"/> sobre o <see cref="ContextoBancoDados"/> (research R-02 do
/// REQ-05). O conflito de concorrência já chega traduzido pelo contexto.
/// </summary>
internal sealed class ArmazenamentoPedidos(ContextoBancoDados contexto) : IArmazenamentoPedidos
{
    /// <summary>Índice único de transportadora e código de rastreio (REQ-04; sem caixa desde o REQ-11).</summary>
    private const string IndiceRastreio = "IX_OrdensEntrega_Transportadora_CodigoRastreio";

    /// <summary>Índice único da ordem por pedido (REQ-04).</summary>
    private const string IndiceOrdemPorPedido = "IX_OrdensEntrega_PedidoId";

    public async Task<IReadOnlyList<Produto>> ObterProdutosAsync(IReadOnlyCollection<Guid> ids, bool paraAlterar, CancellationToken cancellationToken)
    {
        var produtos = paraAlterar ? contexto.Produtos : contexto.Produtos.AsNoTracking();
        return await produtos.Where(p => ids.Contains(p.Id)).ToListAsync(cancellationToken);
    }

    public Task<Pedido?> ObterPedidoAsync(Guid id, bool paraAlterar, CancellationToken cancellationToken)
    {
        var pedidos = paraAlterar ? contexto.Pedidos : contexto.Pedidos.AsNoTracking();
        return pedidos.Include(p => p.Itens).Include(p => p.Historico).Include(p => p.Pagamento).Include(p => p.OrdemEntrega)
            .AsSplitQuery()
            .SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    // A collation das colunas não diferencia maiúsculas e minúsculas (REQ-11, R-06).
    public Task<bool> RastreioEmUsoAsync(string transportadora, string codigoRastreio, Guid pedidoId, CancellationToken cancellationToken) =>
        contexto.OrdensEntrega.AnyAsync(
            o => o.PedidoId != pedidoId && o.Transportadora == transportadora && o.CodigoRastreio == codigoRastreio, cancellationToken);

    public Task<RegistroIdempotencia?> ObterRegistroIdempotenciaAsync(Guid clienteId, Guid chave, CancellationToken cancellationToken) =>
        contexto.ChavesIdempotencia.AsNoTracking().SingleOrDefaultAsync(r => r.ClienteId == clienteId && r.Chave == chave, cancellationToken);

    public void Adicionar(Pedido pedido, RegistroIdempotencia registro, EventoSaida evento)
    {
        contexto.Pedidos.Add(pedido);
        contexto.ChavesIdempotencia.Add(registro);
        contexto.EventosSaida.Add(evento);
    }

    public void Adicionar(EventoSaida evento) => contexto.EventosSaida.Add(evento);

    public async Task SalvarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await contexto.SaveChangesAsync(cancellationToken);
        }
        // A outra requisição com a mesma chave venceu; um deadlock entre elas tem o mesmo
        // tratamento: reler o registro (research R-04).
        catch (DbUpdateException excecao) when (
            ErrosBancoDados.EhViolacaoUnicidade(excecao, ContextoBancoDados.NomeChavePrimariaIdempotencia)
            || (ErrosBancoDados.EhDeadlock(excecao) && GravandoChaveIdempotencia()))
        {
            throw new ChaveIdempotenciaRegistradaException(excecao);
        }
        // Outro pedido gravou o mesmo rastreio entre a conferência e a gravação (REQ-11, R-06).
        catch (DbUpdateException excecao) when (ErrosBancoDados.EhViolacaoUnicidade(excecao, IndiceRastreio))
        {
            throw new RastreioDuplicadoException(excecao);
        }
        // Outra operação (o consumidor de entrega ou outro despacho) criou a ordem do mesmo pedido:
        // quem chamou relê e reavalia, como num conflito de versão (REQ-11, R-07).
        catch (DbUpdateException excecao) when (ErrosBancoDados.EhViolacaoUnicidade(excecao, IndiceOrdemPorPedido))
        {
            var pedido = contexto.ChangeTracker.Entries<OrdemEntrega>().FirstOrDefault(e => e.State == EntityState.Added)?.Entity.PedidoId;
            throw new ConflitoConcorrenciaException(nameof(OrdemEntrega), pedido?.ToString() ?? "", excecao);
        }
        // Deadlock fora de uma transação aberta (o cancelamento pela API): o SQL Server desfez só
        // esta gravação, e quem chamou relê e tenta de novo. Dentro da transação da base de
        // consumo, a transação inteira já foi desfeita: a exceção segue para as novas tentativas
        // da base (REQ-08, research R-07).
        catch (DbUpdateException excecao) when (ErrosBancoDados.EhDeadlock(excecao) && contexto.Database.CurrentTransaction is null)
        {
            var entrada = contexto.ChangeTracker.Entries().FirstOrDefault(e => e.State == EntityState.Modified);
            throw new ConflitoConcorrenciaException(entrada?.Metadata.ClrType.Name ?? "Deadlock", entrada?.Metadata.FindPrimaryKey() is { } chave
                ? string.Join('/', chave.Properties.Select(p => entrada.Property(p.Name).CurrentValue))
                : "", excecao);
        }
    }

    private bool GravandoChaveIdempotencia() =>
        contexto.ChangeTracker.Entries<RegistroIdempotencia>().Any(e => e.State == EntityState.Added);

    public void DescartarAlteracoes() => contexto.ChangeTracker.Clear();
}
