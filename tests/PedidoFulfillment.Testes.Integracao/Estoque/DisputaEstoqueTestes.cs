using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Estoque;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Estoque;

/// <summary>
/// Disputa pelo mesmo produto com SQL Server real (REQ-08, US3, SC-002; research R-03 e R-10).
/// O manipulador roda em paralelo, cada execução no seu escopo e transação, como a base de
/// consumo faz; o Worker processa um evento por vez e não forçaria a intercalação.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class DisputaEstoqueTestes(MensageriaTeste mensageria)
{
    /// <summary>1 execução mais as 3 novas tentativas da base (REQ-06).</summary>
    private const int ExecucoesDaBase = 4;

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    /// <summary>
    /// Executa <see cref="ReservaEstoque"/> como a base: escopo novo, transação, efeito e commit;
    /// em exceção, desfaz e tenta de novo até <see cref="ExecucoesDaBase"/> vezes.
    /// </summary>
    /// <returns>Quantas execuções da base foram necessárias.</returns>
    internal static async Task<int> ReservarComoABaseAsync(
        IServiceProvider servicos, Guid pedidoId, Func<IArmazenamentoPedidos, IArmazenamentoPedidos>? decorar = null)
    {
        var evento = EnvelopeEvento.Criar(pedidoId, new PedidoCriado
        {
            PedidoId = pedidoId,
            Numero = "PED-20261022-000001",
            ClienteId = Guid.CreateVersion7(),
            Itens = [new ItemPedidoCriado { ProdutoId = Guid.CreateVersion7(), Quantidade = 1, PrecoUnitario = 1m }],
            ValorTotal = 1m,
            Moeda = "BRL"
        }, DateTimeOffset.UtcNow);
        for (var execucao = 1; ; execucao++)
        {
            await using var escopo = servicos.CreateAsyncScope();
            var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
            await using var transacao = await contexto.Database.BeginTransactionAsync(Cancelamento);
            var armazenamento = escopo.ServiceProvider.GetRequiredService<IArmazenamentoPedidos>();
            var consumidor = new ReservaEstoque(decorar?.Invoke(armazenamento) ?? armazenamento, TimeProvider.System, NullLogger<ReservaEstoque>.Instance);
            try
            {
                await consumidor.ManipularAsync(evento, Cancelamento);
                await contexto.SaveChangesAsync(Cancelamento);
                await transacao.CommitAsync(Cancelamento);
                return execucao;
            }
            catch (Exception) when (execucao < ExecucoesDaBase)
            {
                await transacao.RollbackAsync(Cancelamento);
            }
        }
    }

    private static async Task<Guid> NovoProdutoAsync(string stringConexao, int estoque)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var produto = new Produto($"DISP-{Guid.NewGuid():N}"[..20], "Produto disputado", null, 10m, estoque, true, DateTimeOffset.UtcNow);
        contexto.Produtos.Add(produto);
        await contexto.SaveChangesAsync(Cancelamento);
        return produto.Id;
    }

    private static async Task<(int Reservados, int Cancelados)> ResultadoAsync(string stringConexao, IEnumerable<Guid> pedidos)
    {
        var ids = string.Join(",", pedidos.Select(p => $"'{p}'"));
        var reservados = await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM Pedidos WHERE Id IN ({ids}) AND EstoqueReservado = 1 AND Status = 'Pendente'");
        var cancelados = await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM Pedidos WHERE Id IN ({ids}) AND EstoqueReservado = 0 AND Status = 'Cancelado'");
        return (reservados, cancelados);
    }

    [Fact]
    public async Task Dois_pedidos_pelo_ultimo_item_reservam_exatamente_um()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidos = new List<Guid>();
        for (var i = 0; i < 2; i++)
        {
            pedidos.Add((await CriadoAsync(cliente, (Cat008, 1))).GetProperty("id").GetGuid());
        }

        await Task.WhenAll(pedidos.Select(p => ReservarComoABaseAsync(api.Services, p)));

        (await ResultadoAsync(stringConexao, pedidos)).Should().Be((1, 1));
        (await EstoqueAsync(stringConexao, Cat008)).Should().Be(0);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM EventosSaida WHERE Tipo LIKE 'estoque.%'")).Should().Be(2);
    }

    [Fact]
    public async Task Oito_pedidos_por_cinco_unidades_reservam_cinco_e_cancelam_tres_em_todas_as_rodadas()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);

        for (var rodada = 1; rodada <= 5; rodada++)
        {
            var produtoId = await NovoProdutoAsync(stringConexao, 5);
            var pedidos = new List<Guid>();
            for (var i = 0; i < 8; i++)
            {
                pedidos.Add((await CriadoAsync(cliente, (produtoId, 1))).GetProperty("id").GetGuid());
            }

            await Task.WhenAll(pedidos.Select(p => ReservarComoABaseAsync(api.Services, p)));

            (await ResultadoAsync(stringConexao, pedidos)).Should().Be((5, 3), $"rodada {rodada}");
            (await EstoqueAsync(stringConexao, produtoId)).Should().Be(0, $"rodada {rodada}");
        }
    }

    [Fact]
    public async Task Conflito_forcado_e_reavaliado_na_mesma_transacao()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2))).GetProperty("id").GetGuid();
        ArmazenamentoComConflito? decorado = null;

        var execucoes = await ReservarComoABaseAsync(api.Services, pedidoId, interno => decorado = new ArmazenamentoComConflito(interno, stringConexao, Cat001));

        execucoes.Should().Be(1, "o conflito é reavaliado dentro da mesma transação, sem nova tentativa da base");

        decorado!.Gravacoes.Should().Be(2, "a primeira gravação perde para a outra operação e a segunda grava");
        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(48);
        (await ReservadoAsync(stringConexao, pedidoId)).Should().BeTrue();
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM EventosSaida WHERE Tipo = 'estoque.reservado'")).Should().Be(1);
    }

    /// <summary>Na primeira gravação, outra operação altera o produto antes (muda a versão dele).</summary>
    private sealed class ArmazenamentoComConflito(IArmazenamentoPedidos interno, string stringConexao, Guid produtoId) : IArmazenamentoPedidos
    {
        public int Gravacoes { get; private set; }

        public Task<IReadOnlyList<Produto>> ObterProdutosAsync(IReadOnlyCollection<Guid> ids, bool paraAlterar, CancellationToken cancellationToken) =>
            interno.ObterProdutosAsync(ids, paraAlterar, cancellationToken);

        public Task<Pedido?> ObterPedidoAsync(Guid id, bool paraAlterar, CancellationToken cancellationToken) =>
            interno.ObterPedidoAsync(id, paraAlterar, cancellationToken);

        public Task<bool> RastreioEmUsoAsync(string transportadora, string codigoRastreio, Guid pedidoId, CancellationToken cancellationToken) =>
            interno.RastreioEmUsoAsync(transportadora, codigoRastreio, pedidoId, cancellationToken);

        public Task<RegistroIdempotencia?> ObterRegistroIdempotenciaAsync(Guid clienteId, Guid chave, CancellationToken cancellationToken) =>
            interno.ObterRegistroIdempotenciaAsync(clienteId, chave, cancellationToken);

        public void Adicionar(Pedido pedido, RegistroIdempotencia registro, EventoSaida evento) => interno.Adicionar(pedido, registro, evento);

        public void Adicionar(EventoSaida evento) => interno.Adicionar(evento);

        public async Task SalvarAsync(CancellationToken cancellationToken)
        {
            if (++Gravacoes == 1)
            {
                // Fora da transação da reserva: muda só a versão do produto.
                await BancoDadosTeste.ExecutarAsync(stringConexao, $"UPDATE Produtos SET AtualizadoEm = SYSDATETIMEOFFSET() WHERE Id = '{produtoId}'");
            }

            await interno.SalvarAsync(cancellationToken);
        }

        public void DescartarAlteracoes() => interno.DescartarAlteracoes();
    }
}
