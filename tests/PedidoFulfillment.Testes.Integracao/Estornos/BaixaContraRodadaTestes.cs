using System.Net;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Pagamentos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estornos.CenarioAcoesEstornos;
using static PedidoFulfillment.Testes.Integracao.Estornos.CenarioEstorno;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Estornos;

/// <summary>
/// A baixa contra a rodada de estornos, com o lock real do SQL Server (REQ-28, US2 cenários 2 e 8,
/// FR-008, FR-009, SC-002, SC-003 e SC-008).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class BaixaContraRodadaTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    /// <summary>Segura o lock da rodada numa sessão própria, como o <c>ProcessadorEstornos</c>.</summary>
    private static async Task<SqlConnection> SegurarLockAsync(string stringConexao)
    {
        var conexao = new SqlConnection(stringConexao);
        await conexao.OpenAsync(Cancelamento);
        await using var comando = new SqlCommand(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;",
            conexao);
        comando.Parameters.AddWithValue("@recurso", ProcessadorEstornos.RecursoLock);
        ((int)(await comando.ExecuteScalarAsync(Cancelamento))!).Should().BeGreaterThanOrEqualTo(0);
        return conexao;
    }

    /// <summary>
    /// Como o processador: devolver a conexão ao pool não encerra a sessão, então o lock é liberado
    /// explicitamente antes.
    /// </summary>
    private static async Task LiberarLockAsync(SqlConnection conexao)
    {
        await using (var comando = new SqlCommand("EXEC sp_releaseapplock @Resource = @recurso, @LockOwner = 'Session';", conexao))
        {
            comando.Parameters.AddWithValue("@recurso", ProcessadorEstornos.RecursoLock);
            await comando.ExecuteNonQueryAsync(Cancelamento);
        }

        await conexao.DisposeAsync();
    }

    [Fact]
    public async Task Baixa_espera_a_rodada_terminar_e_e_gravada()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 1);
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        var conexao = await SegurarLockAsync(stringConexao);

        var baixa = BaixarAsync(admin, estornos[0].EstornoId);
        await Task.Delay(TimeSpan.FromSeconds(1), Cancelamento);
        baixa.IsCompleted.Should().BeFalse("a baixa espera o lock");
        await LiberarLockAsync(conexao);

        (await CorpoOkAsync(await baixa)).GetProperty("situacao").GetString().Should().Be("Baixado", "SC-008");
    }

    [Fact]
    public async Task Baixa_desiste_depois_de_5_segundos_com_409_sem_gravar_nada()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 1);
        var logs = new CapturaLogs();
        await using var api = Api(stringConexao, logs);
        var admin = await AdminAsync(api);
        var conexao = await SegurarLockAsync(stringConexao);
        var inicio = TimeProvider.System.GetTimestamp();

        var resposta = await BaixarAsync(admin, estornos[0].EstornoId);
        await LiberarLockAsync(conexao);

        TimeProvider.System.GetElapsedTime(inicio).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(4.5));
        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var corpo = await CorpoAsync(resposta);
        corpo.GetProperty("codigo").GetString().Should().Be("ESTORNO_RODADA_EM_ANDAMENTO");
        corpo.GetProperty("detail").GetString().Should().Be("Rodada de estornos em andamento; tente de novo em instantes.");
        logs.Registros.Should().Contain(r => r.Evento.Id == 7023);
        (await SituacaoAsync(stringConexao, estornos[0].PedidoId)).Should().Be("EstornoPendente");
    }

    [Fact]
    public async Task Rodada_que_confirma_enquanto_a_baixa_espera_vence_e_a_baixa_responde_409()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 1);
        var pedidoId = estornos[0].PedidoId;
        var gateway = new GatewayEstornoRetido();
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        await using var worker = WorkerEstornando(mensageria, stringConexao, servicos: s => s.AddSingleton<IGatewayPagamento>(gateway));
        worker.CreateClient().Dispose();
        (await gateway.Chamado.Task.WaitAsync(Prazo, Cancelamento)).Should().Be(pedidoId, "a rodada pediu o estorno e segura o lock");

        var baixa = BaixarAsync(admin, estornos[0].EstornoId);
        await Task.Delay(TimeSpan.FromSeconds(1), Cancelamento);
        gateway.Liberar.SetResult();

        var resposta = await baixa;
        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict, "SC-003: o gateway já devolveu");
        (await CodigoAsync(resposta)).Should().Be("ESTORNO_NAO_PENDENTE");
        (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("Estornado");
    }

    [Fact]
    public async Task Depois_da_baixa_a_rodada_nao_chama_mais_o_gateway()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 1);
        await using var api = Api(stringConexao);
        await CorpoOkAsync(await BaixarAsync(await AdminAsync(api), estornos[0].EstornoId));
        var gateway = new GatewayEstornoControlado();
        var logs = new CapturaLogs();

        await using var worker = WorkerEstornando(mensageria, stringConexao, logs, servicos: s => s.AddSingleton<IGatewayPagamento>(gateway));
        worker.CreateClient().Dispose();
        // Rodadas de 1 s: espera três resumos depois da subida.
        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(logs.Registros.Count(r => r.Evento.Id == 7012) >= 3), Prazo)).Should().BeTrue();

        gateway.EstornosDo(estornos[0].PedidoId).Should().Be(0, "SC-002 e US2 cenário 2");
        (await SituacaoAsync(stringConexao, estornos[0].PedidoId)).Should().Be("EstornoBaixado");
    }

    [Fact]
    public async Task Rodada_chamada_durante_a_baixa_pula_sem_chamar_o_gateway()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 1);
        var gateway = new GatewayEstornoControlado();
        var logs = new CapturaLogs();
        await using var api = Api(stringConexao);
        await using var worker = WorkerEstornando(mensageria, stringConexao, logs,
            configuracoes: new Dictionary<string, string?> { ["Estornos:Habilitado"] = "false" },
            servicos: s => s.AddSingleton<IGatewayPagamento>(gateway));
        var processador = worker.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<ProcessadorEstornos>().Single();
        var opcoes = new OpcoesEstornos { Habilitado = true };
        using var escopo = api.Services.CreateScope();
        var bloqueio = escopo.ServiceProvider.GetRequiredService<IBloqueioRodadaEstornos>();

        var conseguiu = await bloqueio.ExecutarAsync(TimeSpan.FromSeconds(5), async ct =>
        {
            await processador.ExecutarRodadaAsync(opcoes, ct);
        }, Cancelamento);

        conseguiu.Should().BeTrue();
        logs.Registros.Should().Contain(r => r.Evento.Id == 7011, "a rodada encontrou o lock com a baixa");
        gateway.EstornosDo(estornos[0].PedidoId).Should().Be(0);
    }

    /// <summary>Segura a resposta do estorno até o teste liberar, com o lock da rodada preso.</summary>
    private sealed class GatewayEstornoRetido : IGatewayPagamento
    {
        public TaskCompletionSource<Guid> Chamado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken) =>
            Task.FromResult(ResultadoCobranca.Aprovada($"SIM-{pedidoId:N}"));

        public Task<ConsultaCobranca> ConsultarCobrancaAsync(Guid pedidoId, CancellationToken cancellationToken) =>
            Task.FromResult(ConsultaCobranca.NaoEncontrada);

        public async Task<ResultadoEstorno> EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken cancellationToken)
        {
            Chamado.TrySetResult(pedidoId);
            await Liberar.Task.WaitAsync(cancellationToken);
            return ResultadoEstorno.Confirmar($"SIM-EST-{pedidoId:N}");
        }
    }
}
