using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Pagamentos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estornos.CenarioEstorno;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Estornos;

/// <summary>
/// Rodada dos estornos pendentes no Worker, com SQL Server e RabbitMQ reais (REQ-13, US1
/// cenários 2, 6 e 7, FR-002 a FR-005 e FR-007).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class ProcessamentoEstornosTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static Action<IServiceCollection> Com(IGatewayPagamento gateway) => servicos => servicos.AddSingleton(gateway);

    private static Task<bool> EsperarLogsAsync(CapturaLogs logs, int eventId, int quantidade = 1) =>
        MensageriaTeste.EsperarAteAsync(() => Task.FromResult(logs.Registros.Count(r => r.Evento.Id == eventId) >= quantidade), Prazo);

    /// <summary>Pedido pago e cancelado pela operação, com o estorno pendente e o Worker parado.</summary>
    private async Task<(string StringConexao, Guid PedidoId)> CanceladoPagoAsync()
    {
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await PedidoPagoAsync(mensageria, stringConexao, cliente);
        await CancelarComoOperacaoAsync(await OperadorAsync(api), pedidoId);
        (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("EstornoPendente", "com o Worker parado, o estorno espera");
        return (stringConexao, pedidoId);
    }

    [Fact]
    public async Task Estorno_pendente_e_confirmado_na_primeira_rodada_depois_que_o_Worker_sobe()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId) = await CanceladoPagoAsync();
        var gateway = new GatewayEstornoControlado();
        var logs = new CapturaLogs();

        await using var worker = WorkerEstornando(mensageria, stringConexao, logs, servicos: Com(gateway));
        worker.CreateClient().Dispose();

        (await EsperarSituacaoAsync(stringConexao, pedidoId, "Estornado")).Should().BeTrue("FR-002");
        gateway.EstornosDo(pedidoId).Should().Be(1);
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT IdentificadorEstorno FROM Pagamentos WHERE PedidoId = '{pedidoId}'"))
            .Should().Be($"SIM-EST-{pedidoId:N}");
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM Pagamentos WHERE PedidoId = '{pedidoId}' AND EstornadoEm IS NOT NULL"))
            .Should().Be(1);
        (await EsperarLogsAsync(logs, 7006)).Should().BeTrue();
        (await EsperarLogsAsync(logs, 7012)).Should().BeTrue();
        logs.Registros.Where(r => r.Evento.Id is >= 7005 and <= 7012).Should().NotContain(r => r.Texto.Contains("SIM-", StringComparison.Ordinal),
            "FR-016: nenhum log traz transação ou estorno");

        // REQ-15, US2 cenário 1: um pagamento.estornado, com os identificadores do gateway e o instante gravado.
        var estornado = (await EventosOutbox.ListarAsync<PagamentoEstornado>(stringConexao, pedidoId)).Should().ContainSingle().Subject.Dados;
        estornado.IdentificadorEstorno.Should().Be($"SIM-EST-{pedidoId:N}");
        estornado.IdentificadorTransacao.Should().Be(await BancoDadosTeste.ConsultarAsync<string>(stringConexao,
            $"SELECT IdentificadorTransacao FROM Pagamentos WHERE PedidoId = '{pedidoId}'"));
        estornado.Valor.Should().Be(await BancoDadosTeste.ConsultarAsync<decimal>(stringConexao, $"SELECT Valor FROM Pagamentos WHERE PedidoId = '{pedidoId}'"));
        estornado.EstornadoEm.Should().Be(await BancoDadosTeste.ConsultarAsync<DateTimeOffset>(stringConexao,
            $"SELECT EstornadoEm FROM Pagamentos WHERE PedidoId = '{pedidoId}'"));
    }

    [Fact]
    public async Task Falha_do_gateway_mantem_o_estorno_pendente_conta_as_tentativas_alerta_e_depois_confirma()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId) = await CanceladoPagoAsync();
        var gateway = new GatewayEstornoControlado { Modo = GatewayEstornoControlado.ModoEstorno.Falhar };
        var logs = new CapturaLogs();
        var configuracoes = new Dictionary<string, string?> { ["Estornos:LimiteAlertaSegundos"] = "1", ["Estornos:TentativasAlerta"] = "2" };

        await using var worker = WorkerEstornando(mensageria, stringConexao, logs, configuracoes, Com(gateway));
        worker.CreateClient().Dispose();

        (await EsperarLogsAsync(logs, 7008)).Should().BeTrue("US1 cenário 7: depois do prazo e das tentativas, a falha é erro");
        logs.Registros.Should().Contain(r => r.Evento.Id == 7007, "US1 cenário 6: a primeira falha é aviso");
        (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("EstornoPendente");
        (await TentativasAsync(stringConexao, pedidoId)).Should().BeGreaterThanOrEqualTo(2);
        (await Estoque.CenarioEstoque.StatusAsync(stringConexao, pedidoId)).Should().Be("Cancelado");
        (await EventosOutbox.ListarAsync<PagamentoEstornado>(stringConexao, pedidoId)).Should().BeEmpty("REQ-15, FR-006: falhas não geram evento");

        gateway.Modo = GatewayEstornoControlado.ModoEstorno.Confirmar;

        (await EsperarSituacaoAsync(stringConexao, pedidoId, "Estornado")).Should().BeTrue("o estorno nunca desiste (FR-003)");
        (await EventosOutbox.ListarAsync<PagamentoEstornado>(stringConexao, pedidoId)).Should().ContainSingle("REQ-15, US2 cenário 2");
    }

    [Fact]
    public async Task Recusa_do_gateway_e_tratada_como_falha()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId) = await CanceladoPagoAsync();
        var gateway = new GatewayEstornoControlado { Modo = GatewayEstornoControlado.ModoEstorno.Recusar };
        var logs = new CapturaLogs();

        await using var worker = WorkerEstornando(mensageria, stringConexao, logs, servicos: Com(gateway));
        worker.CreateClient().Dispose();

        (await EsperarLogsAsync(logs, 7007)).Should().BeTrue();
        logs.Registros.First(r => r.Evento.Id == 7007).Texto.Should().Contain("Recusado");
        (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("EstornoPendente");
    }

    [Fact]
    public async Task Dois_Workers_estornam_o_pedido_uma_unica_vez()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId) = await CanceladoPagoAsync();
        var gateway = new GatewayEstornoControlado();
        var logs = new CapturaLogs();

        await using var primeiro = WorkerEstornando(mensageria, stringConexao, logs, servicos: Com(gateway));
        await using var segundo = WorkerEstornando(mensageria, stringConexao, logs, servicos: Com(gateway));
        await Task.WhenAll(Task.Run(() => primeiro.CreateClient().Dispose(), Cancelamento), Task.Run(() => segundo.CreateClient().Dispose(), Cancelamento));

        (await EsperarSituacaoAsync(stringConexao, pedidoId, "Estornado")).Should().BeTrue();
        (await EsperarLogsAsync(logs, 7012, quantidade: 4)).Should().BeTrue("as duas instâncias seguem rodando");
        gateway.EstornosDo(pedidoId).Should().Be(1, "FR-005");
        logs.Registros.Count(r => r.Evento.Id == 7006).Should().Be(1);
        (await EventosOutbox.ListarAsync<PagamentoEstornado>(stringConexao, pedidoId)).Should().ContainSingle("REQ-15, SC-002");
    }

    [Fact]
    public async Task Com_o_lock_ocupado_o_Worker_pula_a_rodada()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId) = await CanceladoPagoAsync();
        var gateway = new GatewayEstornoControlado();
        var logs = new CapturaLogs();

        // Outra "instância" segura o lock numa sessão própria, fora do pool.
        var semPool = new SqlConnectionStringBuilder(stringConexao) { Pooling = false }.ConnectionString;
        var conexao = new SqlConnection(semPool);
        await conexao.OpenAsync(Cancelamento);
        try
        {
            await using (var comando = new SqlCommand(
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;", conexao))
            {
                comando.Parameters.AddWithValue("@recurso", ProcessadorEstornos.RecursoLock);
                ((int)(await comando.ExecuteScalarAsync(Cancelamento))!).Should().BeGreaterThanOrEqualTo(0);
            }

            await using var worker = WorkerEstornando(mensageria, stringConexao, logs, servicos: Com(gateway));
            worker.CreateClient().Dispose();

            (await EsperarLogsAsync(logs, 7011, quantidade: 2)).Should().BeTrue("a instância não consegue o lock");
            gateway.EstornosDo(pedidoId).Should().Be(0);

            await conexao.DisposeAsync();
            (await EsperarSituacaoAsync(stringConexao, pedidoId, "Estornado")).Should().BeTrue("com o lock livre, a rodada seguinte estorna");
        }
        finally
        {
            await conexao.DisposeAsync();
        }
    }

    [Fact]
    public async Task Estorno_que_falha_sempre_vai_para_o_fim_do_lote()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidos = await PedidosPagosAsync(mensageria, stringConexao, cliente, 2);
        var operador = await OperadorAsync(api);
        var gateway = new GatewayEstornoControlado();
        gateway.FalharSemprePara(pedidos[0]);
        var logs = new CapturaLogs();

        // O que falha sempre é cancelado primeiro: é o mais antigo da fila.
        await CancelarComoOperacaoAsync(operador, pedidos[0]);
        await using (var worker = WorkerEstornando(mensageria, stringConexao, logs, new Dictionary<string, string?> { ["Estornos:TamanhoLote"] = "1" }, Com(gateway)))
        {
            worker.CreateClient().Dispose();
            (await EsperarLogsAsync(logs, 7007)).Should().BeTrue();
            await CancelarComoOperacaoAsync(operador, pedidos[1]);

            (await EsperarSituacaoAsync(stringConexao, pedidos[1], "Estornado")).Should().BeTrue("o pendente novo vem antes do que já falhou");
        }

        (await SituacaoAsync(stringConexao, pedidos[0])).Should().Be("EstornoPendente");
        gateway.EstornosDo(pedidos[1]).Should().Be(1);
    }

    [Fact]
    public async Task Falha_da_rodada_inteira_e_registrada_e_a_rodada_seguinte_roda()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using (var api = Api(stringConexao))
        {
            // A API prepara o banco; o Worker não aplica migrations.
            api.CreateClient().Dispose();
        }

        var logs = new CapturaLogs();

        await using var worker = WorkerEstornando(mensageria, stringConexao, logs,
            servicos: servicos => servicos.AddScoped<IConsultaEstornosPendentes, ConsultaQueFalha>());
        worker.CreateClient().Dispose();

        (await EsperarLogsAsync(logs, 7010, quantidade: 2)).Should().BeTrue("a falha não derruba o Worker, e a próxima rodada tenta de novo");
        logs.Registros.First(r => r.Evento.Id == 7010).Texto.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public async Task Estorno_confirmado_e_perdido_antes_da_gravacao_recebe_o_mesmo_estorno_na_rodada_seguinte()
    {
        // REQ-13, US4 cenário 3: o gateway simulado é idempotente por pedido.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId) = await CanceladoPagoAsync();
        var gateway = new GatewayQuePerdeAPrimeiraResposta(new GatewayPagamentoSimulado(Options.Create(new OpcoesGatewayPagamento())));

        await using var worker = WorkerEstornando(mensageria, stringConexao, servicos: Com(gateway));
        worker.CreateClient().Dispose();

        (await EsperarSituacaoAsync(stringConexao, pedidoId, "Estornado")).Should().BeTrue();
        gateway.PrimeiraResposta.Should().NotBeNull();
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT IdentificadorEstorno FROM Pagamentos WHERE PedidoId = '{pedidoId}'"))
            .Should().Be(gateway.PrimeiraResposta!.IdentificadorEstorno, "a segunda chamada devolve o mesmo estorno");
        // REQ-15, US2 cenário 3: só a gravação que deu certo leva o evento.
        (await EventosOutbox.ListarAsync<PagamentoEstornado>(stringConexao, pedidoId)).Should().ContainSingle()
            .Which.Dados.IdentificadorEstorno.Should().Be(gateway.PrimeiraResposta.IdentificadorEstorno);
    }

    /// <summary>Repassa ao gateway real, mas "perde" a primeira resposta do estorno, como um Worker que caiu antes de gravar.</summary>
    private sealed class GatewayQuePerdeAPrimeiraResposta(IGatewayPagamento interno) : IGatewayPagamento
    {
        public ResultadoEstorno? PrimeiraResposta { get; private set; }

        public Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken) =>
            interno.CobrarAsync(pedidoId, valor, cancellationToken);

        public async Task<ResultadoEstorno> EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken cancellationToken)
        {
            var resposta = await interno.EstornarAsync(pedidoId, identificadorTransacao, valor, cancellationToken);
            if (PrimeiraResposta is null)
            {
                PrimeiraResposta = resposta;
                throw new IOException("Conexão perdida depois da confirmação do gateway.");
            }

            return resposta;
        }
    }

    private sealed class ConsultaQueFalha : IConsultaEstornosPendentes
    {
        public Task<IReadOnlyList<EstornoPendente>> ListarAsync(int quantidade, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Banco indisponível na consulta.");
    }
}
