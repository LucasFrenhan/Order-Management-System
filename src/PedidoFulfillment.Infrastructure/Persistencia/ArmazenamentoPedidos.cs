using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pagamentos;
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
        ExigirEventosDeCancelamentoEEstorno();
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

    /// <summary>
    /// Guarda do REQ-15 (R-03): um pedido que passa a Cancelado precisa de um <c>pedido.cancelado</c>
    /// do mesmo pedido na mesma gravação, e um pagamento que passa a Estornado, de um
    /// <c>pagamento.estornado</c>. Faltando, é erro de programação: nada é gravado, e a exceção segue
    /// o caminho de erro de quem chamou (no Worker, novas tentativas e DLQ; na API, 500). Fica aqui, e
    /// não no <see cref="ContextoBancoDados"/>, porque é regra do fluxo da aplicação: os testes
    /// semeiam pedidos cancelados direto pelo contexto, e entidades novas não são conferidas.
    /// </summary>
    /// <exception cref="InvalidOperationException">Falta o evento de um cancelamento ou de um estorno.</exception>
    private void ExigirEventosDeCancelamentoEEstorno()
    {
        var cancelados = contexto.ChangeTracker.Entries<Pedido>()
            .Where(e => e.State == EntityState.Modified
                && e.Property(p => p.Status).OriginalValue != StatusPedido.Cancelado
                && e.Property(p => p.Status).CurrentValue == StatusPedido.Cancelado)
            .Select(e => (e.Entity.Id, Descricao: $"pedido {e.Entity.Numero}"));
        var estornados = contexto.ChangeTracker.Entries<Pagamento>()
            .Where(e => e.State == EntityState.Modified
                && e.Property(p => p.Situacao).OriginalValue == SituacaoPagamento.EstornoPendente
                && e.Property(p => p.Situacao).CurrentValue == SituacaoPagamento.Estornado)
            .Select(e => (Id: e.Entity.PedidoId, Descricao: $"pagamento do pedido {e.Entity.PedidoId}"));

        Exigir(cancelados.ToList(), TiposEvento.PedidoCancelado);
        Exigir(estornados.ToList(), TiposEvento.PagamentoEstornado);
    }

    private void Exigir(IReadOnlyList<(Guid PedidoId, string Descricao)> alterados, string tipo)
    {
        if (alterados.Count == 0)
        {
            return;
        }

        var comEvento = contexto.ChangeTracker.Entries<EventoSaida>()
            .Where(e => e.State == EntityState.Added && e.Entity.Tipo == tipo)
            .Select(e => IdCorrelacao(e.Entity.Conteudo))
            .ToHashSet();
        if (alterados.FirstOrDefault(a => !comEvento.Contains(a.PedidoId)) is { Descricao: { } descricao })
        {
            throw new InvalidOperationException($"O {descricao} muda sem o evento {tipo} na mesma gravação (REQ-15).");
        }
    }

    private static Guid IdCorrelacao(string conteudo)
    {
        using var documento = JsonDocument.Parse(conteudo);
        return documento.RootElement.GetProperty("idCorrelacao").GetGuid();
    }

    public void DescartarAlteracoes() => contexto.ChangeTracker.Clear();
}
