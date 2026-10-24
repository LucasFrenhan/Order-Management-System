using FluentAssertions;
using Microsoft.Data.SqlClient;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Infrastructure.Reconciliacao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Entregas.CenarioEntrega;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;
using static PedidoFulfillment.Testes.Integracao.Reconciliacao.CenarioReconciliacao;

namespace PedidoFulfillment.Testes.Integracao.Reconciliacao;

/// <summary>
/// Reconciliação repetida e em duas instâncias do Worker (REQ-12, US2 cenário 6, FR-009, SC-003;
/// research R-08): o pedido avança uma vez só, com um único evento de saída.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class ReconciliacaoConcorrenteTestes(MensageriaTeste mensageria)
{
    private static readonly TimeSpan UmaHora = TimeSpan.FromHours(1);

    [Fact]
    public async Task Duas_instancias_produzem_um_efeito_so_por_pedido()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidos = await ParadosNaEntregaAsync(mensageria, stringConexao, cliente, 5);
        foreach (var pedidoId in pedidos)
        {
            await EnvelhecerAsync(stringConexao, pedidoId, UmaHora);
        }

        using var logs = new CapturaLogs();

        try
        {
            await using var primeiro = WorkerReconciliando(mensageria, stringConexao, logs);
            await using var segundo = WorkerReconciliando(mensageria, stringConexao, logs);
            await Task.WhenAll(
                Task.Run(() => primeiro.CreateClient().Dispose(), TestContext.Current.CancellationToken),
                Task.Run(() => segundo.CreateClient().Dispose(), TestContext.Current.CancellationToken));

            foreach (var pedidoId in pedidos)
            {
                (await EsperarOrdemAsync(stringConexao, pedidoId, Prazo)).Should().BeTrue();
            }

            // Mais alguns ciclos, para uma segunda gravação aparecer se existisse.
            var ciclos = logs.Registros.Count(r => r.Evento.Id == 9004);
            (await EsperarLogsAsync(logs, 9004, quantidade: ciclos + 3)).Should().BeTrue();
            foreach (var pedidoId in pedidos)
            {
                (await OrdensAsync(stringConexao, pedidoId)).Should().Be(1);
                (await EventosAsync(stringConexao, pedidoId, TiposEvento.EntregaCriada)).Should().Be(1);
                (await EntradasHistoricoAsync(stringConexao, pedidoId)).Should().Be(3, "criação, Processando e ProntoParaEnvio, uma vez cada");
            }
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Com_o_lock_ocupado_a_instancia_pula_o_ciclo_e_reconcilia_quando_ele_e_liberado()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await ParadoNaEntregaAsync(mensageria, stringConexao, cliente);
        await EnvelhecerAsync(stringConexao, pedidoId, UmaHora);
        using var logs = new CapturaLogs();

        // Outra "instância" segura o lock numa sessão própria, fora do pool.
        var semPool = new SqlConnectionStringBuilder(stringConexao) { Pooling = false }.ConnectionString;
        var conexao = new SqlConnection(semPool);
        await conexao.OpenAsync(TestContext.Current.CancellationToken);
        try
        {
            await using (var comando = new SqlCommand(
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;", conexao))
            {
                comando.Parameters.AddWithValue("@recurso", ReconciliadorPedidos.RecursoLock);
                ((int)(await comando.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).Should().BeGreaterThanOrEqualTo(0);
            }

            await using var worker = WorkerReconciliando(mensageria, stringConexao, logs);
            worker.CreateClient().Dispose();

            (await EsperarLogsAsync(logs, 9006, quantidade: 2)).Should().BeTrue("a instância não consegue o lock");
            (await OrdensAsync(stringConexao, pedidoId)).Should().Be(0);
            logs.Registros.Should().NotContain(r => r.Evento.Id == 9004);

            await conexao.DisposeAsync();
            (await EsperarOrdemAsync(stringConexao, pedidoId, Prazo)).Should().BeTrue("com o lock livre, o ciclo seguinte reconcilia");
        }
        finally
        {
            await conexao.DisposeAsync();
            await LimparFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Ciclos_seguidos_sobre_o_mesmo_pedido_produzem_um_efeito_so()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await ParadoNaEntregaAsync(mensageria, stringConexao, cliente);
        await EnvelhecerAsync(stringConexao, pedidoId, UmaHora);
        using var logs = new CapturaLogs();

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao, logs);
            worker.CreateClient().Dispose();

            (await EsperarOrdemAsync(stringConexao, pedidoId, Prazo)).Should().BeTrue();
            var ciclos = logs.Registros.Count(r => r.Evento.Id == 9004);
            (await EsperarLogsAsync(logs, 9004, quantidade: ciclos + 2)).Should().BeTrue();
            (await OrdensAsync(stringConexao, pedidoId)).Should().Be(1);
            (await EventosAsync(stringConexao, pedidoId, TiposEvento.EntregaCriada)).Should().Be(1);
            logs.Registros.Count(r => r.Evento.Id == 9001).Should().Be(1);
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }
}
