using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Notificacoes;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Domain.Usuarios;

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

    /// <summary>Entradas do histórico e pagamentos que já geraram aviso nesta unidade de trabalho (REQ-18).</summary>
    private readonly HashSet<object> avisados = new(ReferenceEqualityComparer.Instance);

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

    public async Task<byte[]?> ObterVersaoAsync(Guid id, CancellationToken cancellationToken) =>
        await contexto.Pedidos.Where(p => p.Id == id)
            .Select(p => EF.Property<byte[]>(p, ContextoBancoDados.Versao))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Paginação por chave sobre <c>(CriadoEm DESC, Numero DESC)</c> (REQ-21, research R-01, R-02
    /// e R-07): um <c>TOP</c> pelos índices da listagem, com projeção direta para o resumo, sem
    /// carregar itens, histórico, pagamento nem entrega (FR-019).
    /// </summary>
    public async Task<IReadOnlyList<ResumoPedidoResponseDTO>> ListarResumosAsync(
        FiltroListagemPedidos filtro, PosicaoCursor? posicao, int quantidade, CancellationToken cancellationToken)
    {
        var pedidos = Filtrar(contexto.Pedidos.AsNoTracking(), filtro);

        IOrderedQueryable<Pedido> ordenados;
        if (posicao is { Sentido: SentidoCursor.Anterior } anterior)
        {
            var (c, n) = (anterior.CriadoEm, anterior.Numero);
            pedidos = anterior.Inclusiva
                ? pedidos.Where(p => p.CriadoEm > c || (p.CriadoEm == c && string.Compare(p.Numero, n) >= 0))
                : pedidos.Where(p => p.CriadoEm > c || (p.CriadoEm == c && string.Compare(p.Numero, n) > 0));
            ordenados = pedidos.OrderBy(p => p.CriadoEm).ThenBy(p => p.Numero);
        }
        else
        {
            if (posicao is { } proxima)
            {
                var (c, n) = (proxima.CriadoEm, proxima.Numero);
                pedidos = proxima.Inclusiva
                    ? pedidos.Where(p => p.CriadoEm < c || (p.CriadoEm == c && string.Compare(p.Numero, n) <= 0))
                    : pedidos.Where(p => p.CriadoEm < c || (p.CriadoEm == c && string.Compare(p.Numero, n) < 0));
            }

            ordenados = pedidos.OrderByDescending(p => p.CriadoEm).ThenByDescending(p => p.Numero);
        }

        var resumos = await ordenados.Take(quantidade)
            .Select(p => new ResumoPedidoResponseDTO
            {
                Id = p.Id,
                Numero = p.Numero,
                ClienteId = p.ClienteId,
                Status = p.Status,
                ValorTotal = p.ValorTotal,
                Moeda = p.Moeda,
                QuantidadeItens = p.Itens.Count,
                CriadoEm = p.CriadoEm,
                AtualizadoEm = p.AtualizadoEm
            })
            .ToListAsync(cancellationToken);

        if (posicao?.Sentido == SentidoCursor.Anterior)
        {
            resumos.Reverse();
        }

        return resumos;
    }

    private static IQueryable<Pedido> Filtrar(IQueryable<Pedido> pedidos, FiltroListagemPedidos filtro)
    {
        if (filtro.ClienteId is { } clienteId)
        {
            pedidos = pedidos.Where(p => p.ClienteId == clienteId);
        }

        if (filtro.Status.Count > 0)
        {
            var status = filtro.Status.ToList();
            pedidos = pedidos.Where(p => status.Contains(p.Status));
        }

        if (filtro.CriadoDe is { } criadoDe)
        {
            pedidos = pedidos.Where(p => p.CriadoEm >= criadoDe);
        }

        if (filtro.CriadoAte is { } criadoAte)
        {
            pedidos = pedidos.Where(p => p.CriadoEm <= criadoAte);
        }

        if (filtro.AtualizadoDe is { } atualizadoDe)
        {
            pedidos = pedidos.Where(p => p.AtualizadoEm >= atualizadoDe);
        }

        if (filtro.AtualizadoAte is { } atualizadoAte)
        {
            pedidos = pedidos.Where(p => p.AtualizadoEm <= atualizadoAte);
        }

        return pedidos;
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
        await AdicionarAvisosAsync(cancellationToken);
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
    /// <c>pagamento.estornado</c>; um que passa a EstornoBaixado, de um <c>pagamento.estorno-baixado</c>; e
    /// um estorno que passa do limite de alerta, de um <c>pagamento.estorno-atrasado</c> (REQ-28). Faltando,
    /// é erro de programação: nada é gravado, e a exceção segue
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
        // A baixa manual exige o pagamento.estorno-baixado (REQ-28, R-07).
        var baixados = contexto.ChangeTracker.Entries<Pagamento>()
            .Where(e => e.State == EntityState.Modified
                && e.Property(p => p.Situacao).OriginalValue == SituacaoPagamento.EstornoPendente
                && e.Property(p => p.Situacao).CurrentValue == SituacaoPagamento.EstornoBaixado)
            .Select(e => (Id: e.Entity.PedidoId, Descricao: $"pagamento do pedido {e.Entity.PedidoId}"));

        Exigir(cancelados.ToList(), TiposEvento.PedidoCancelado);
        Exigir(estornados.ToList(), TiposEvento.PagamentoEstornado);
        Exigir(baixados.ToList(), TiposEvento.PagamentoEstornoBaixado);
        // O estorno que passa do limite de alerta exige o pagamento.estorno-atrasado (REQ-28, R-06).
        var atrasados = contexto.ChangeTracker.Entries<Pagamento>()
            .Where(e => e.State == EntityState.Modified
                && e.Property(p => p.EstornoAtrasadoEm).OriginalValue == null
                && e.Property(p => p.EstornoAtrasadoEm).CurrentValue != null)
            .Select(e => (Id: e.Entity.PedidoId, Descricao: $"pagamento do pedido {e.Entity.PedidoId}"));
        Exigir(atrasados.ToList(), TiposEvento.PagamentoEstornoAtrasado);
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

    /// <summary>
    /// Avisos do REQ-18 (R-14): cada entrada nova do histórico com status anterior vira um
    /// <c>pedido.status-alterado</c> no outbox e um aviso; cada estorno confirmado ou baixado (REQ-28) vira um aviso com
    /// o identificador do <c>pagamento.estornado</c> ou do <c>pagamento.estorno-baixado</c> da mesma gravação (a guarda acima garante que ele
    /// existe). Num conflito, o <see cref="DescartarAlteracoes"/> leva tudo junto, e a nova tentativa
    /// gera de novo.
    /// </summary>
    /// <exception cref="InvalidOperationException">O histórico do pedido que mudou não foi carregado.</exception>
    private async Task AdicionarAvisosAsync(CancellationToken cancellationToken)
    {
        // Uma gravação que falhou sem descarte e é tentada de novo não gera o aviso duas vezes.
        var mudancas = contexto.ChangeTracker.Entries<HistoricoStatusPedido>()
            .Where(e => e.State == EntityState.Added && e.Entity.StatusAnterior is not null)
            .Select(e => e.Entity)
            .Where(avisados.Add)
            .ToList();
        // A confirmação pelo gateway e a baixa manual (REQ-28, FR-010a) avisam o Cliente do mesmo jeito,
        // cada uma com o id do seu evento.
        var estornados = contexto.ChangeTracker.Entries<Pagamento>()
            .Where(e => e.State == EntityState.Modified
                && e.Property(p => p.Situacao).OriginalValue == SituacaoPagamento.EstornoPendente
                && e.Property(p => p.Situacao).CurrentValue is SituacaoPagamento.Estornado or SituacaoPagamento.EstornoBaixado)
            .Where(e => avisados.Add(e.Entity))
            .Select(e => (e.Entity.PedidoId, Tipo: e.Entity.Situacao == SituacaoPagamento.Estornado
                ? TiposEvento.PagamentoEstornado
                : TiposEvento.PagamentoEstornoBaixado))
            .ToList();
        if (mudancas.Count == 0 && estornados.Count == 0)
        {
            return;
        }

        var operacao = await AutoresDeOperacaoAsync(mudancas, cancellationToken);
        foreach (var entrada in mudancas)
        {
            var pedido = PedidoRastreado(entrada.PedidoId);
            if (!contexto.Entry(pedido).Collection(p => p.Historico).IsLoaded)
            {
                throw new InvalidOperationException($"O pedido {pedido.Numero} muda de status sem o histórico carregado (REQ-18).");
            }

            var sequencia = pedido.Historico.ToList().IndexOf(entrada) + 1;
            var origem = entrada.AutorId switch
            {
                null => OrigemMudancaStatus.Worker,
                { } autor when operacao.Contains(autor) => OrigemMudancaStatus.Operacao,
                _ => OrigemMudancaStatus.Cliente
            };
            var (evento, aviso) = FabricaAvisos.StatusAlterado(pedido, entrada, sequencia, origem);
            contexto.EventosSaida.Add(evento);
            contexto.AvisosPedido.Add(aviso);
        }

        foreach (var (pedidoId, tipo) in estornados)
        {
            var estornado = contexto.ChangeTracker.Entries<EventoSaida>()
                .Where(e => e.State == EntityState.Added && e.Entity.Tipo == tipo)
                .Select(e => e.Entity)
                .First(e => IdCorrelacao(e.Conteudo) == pedidoId);
            contexto.AvisosPedido.Add(FabricaAvisos.EstornoConfirmado(PedidoRastreado(pedidoId), estornado.IdEvento, estornado.CriadoEm));
        }
    }

    /// <summary>
    /// Os autores com perfil de operação (Admin ou OperadorLogistica): a mesma regra do
    /// <c>ServicoPedidos</c> no REQ-15, que cobre quem também é Cliente. Só consulta com autor, ou
    /// seja, nos caminhos da API.
    /// </summary>
    private async Task<HashSet<Guid>> AutoresDeOperacaoAsync(IReadOnlyCollection<HistoricoStatusPedido> mudancas, CancellationToken cancellationToken)
    {
        var autores = mudancas.Where(m => m.AutorId is not null).Select(m => m.AutorId!.Value).Distinct().ToList();
        if (autores.Count == 0)
        {
            return [];
        }

        string[] perfisOperacao = [nameof(PerfilUsuario.Admin), nameof(PerfilUsuario.OperadorLogistica)];
        var deOperacao = await (
                from vinculo in contexto.UserRoles
                join perfil in contexto.Roles on vinculo.RoleId equals perfil.Id
                where autores.Contains(vinculo.UserId) && perfisOperacao.Contains(perfil.Name!)
                select vinculo.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);
        return [.. deOperacao];
    }

    private Pedido PedidoRastreado(Guid pedidoId) =>
        contexto.ChangeTracker.Entries<Pedido>().Select(e => e.Entity).FirstOrDefault(p => p.Id == pedidoId)
        ?? throw new InvalidOperationException($"O pedido {pedidoId} muda sem estar carregado (REQ-18).");

    private static Guid IdCorrelacao(string conteudo)
    {
        using var documento = JsonDocument.Parse(conteudo);
        return documento.RootElement.GetProperty("idCorrelacao").GetGuid();
    }

    public void DescartarAlteracoes()
    {
        contexto.ChangeTracker.Clear();
        avisados.Clear();
    }
}
