using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estornos.CenarioAcoesEstornos;
using static PedidoFulfillment.Testes.Integracao.Estornos.CenarioEstorno;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Estornos;

/// <summary>
/// Tentativa forçada do estorno pela operação, com SQL Server real e o Worker (REQ-28, US4, FR-011 a
/// FR-014, SC-005).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class TentativaForcadaTestes(MensageriaTeste mensageria)
{
    private static Task<bool> EsperarLogsAsync(CapturaLogs logs, int eventId, int quantidade = 1) =>
        MensageriaTeste.EsperarAteAsync(() => Task.FromResult(logs.Registros.Count(r => r.Evento.Id == eventId) >= quantidade), Prazo);

    [Fact]
    public async Task Operacao_pede_a_tentativa_e_recebe_202_repetir_nao_duplica_e_os_acessos_sao_conferidos()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 2);
        var (pendente, confirmado) = (estornos[0], estornos[1]);
        await ConfirmadoAsync(stringConexao, confirmado.EstornoId);
        var logs = new CapturaLogs();
        await using var api = Api(stringConexao, logs);
        var operador = await OperadorAsync(api);
        var admin = await AdminAsync(api);
        var (cliente, _, _) = await ClienteAsync(api);

        // US4 cenário 1.
        var resposta = await ForcarAsync(operador, pendente.EstornoId);
        var corpo = await CorpoOkAsync(resposta, HttpStatusCode.Accepted);
        resposta.Headers.Location!.ToString().Should().Be($"/api/v1/estornos/{pendente.EstornoId}");
        var marca = corpo.GetProperty("tentativaForcadaEm").GetDateTimeOffset();
        (await CorpoOkAsync(await ObterAsync(operador, pendente.EstornoId))).GetProperty("tentativaForcadaEm").GetDateTimeOffset().Should().Be(marca);

        // US4 cenário 5: de novo, pelo Admin, sem duplicar.
        var deNovo = await CorpoOkAsync(await ForcarAsync(admin, pendente.EstornoId), HttpStatusCode.Accepted);
        deNovo.GetProperty("tentativaForcadaEm").GetDateTimeOffset().Should().Be(marca);
        logs.Registros.Count(r => r.Evento.Id == 7021).Should().Be(2, "cada pedido fica no log, com o autor");

        // US4 cenários 6 e 7.
        var deConfirmado = await ForcarAsync(operador, confirmado.EstornoId);
        deConfirmado.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(deConfirmado)).Should().Be("ESTORNO_NAO_PENDENTE");
        (await ForcarAsync(operador, Guid.CreateVersion7())).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ForcarAsync(cliente, pendente.EstornoId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ForcarAsync(api.CreateClient(), pendente.EstornoId)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task O_estorno_forcado_e_tentado_primeiro_mesmo_fora_do_lote()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 4);
        var ultimo = estornos[^1];
        await using var api = Api(stringConexao);
        await CorpoOkAsync(await ForcarAsync(await OperadorAsync(api), ultimo.EstornoId), HttpStatusCode.Accepted);
        var gateway = new GatewayOrdenado();

        await using (var worker = WorkerEstornando(mensageria, stringConexao,
                         configuracoes: new Dictionary<string, string?> { ["Estornos:TamanhoLote"] = "2" },
                         servicos: s => s.AddSingleton<IGatewayPagamento>(gateway)))
        {
            worker.CreateClient().Dispose();
            (await EsperarSituacaoAsync(stringConexao, ultimo.PedidoId, "Estornado")).Should().BeTrue();
        }

        gateway.Chamadas.First().Should().Be(ultimo.PedidoId, "SC-005: o último da fila furou o lote de 2");
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM Pagamentos WHERE Id = '{ultimo.EstornoId}' AND TentativaEstornoForcadaEm IS NULL")).Should().Be(1);
    }

    [Fact]
    public async Task A_tentativa_forcada_que_falha_conta_e_limpa_a_marca_e_com_o_circuito_aberto_a_marca_fica()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 2);
        var (falha, adiado) = (estornos[0], estornos[1]);
        await using var api = Api(stringConexao);
        var operador = await OperadorAsync(api);
        await CorpoOkAsync(await ForcarAsync(operador, falha.EstornoId), HttpStatusCode.Accepted);

        // US4 cenário 3: a falha conta como tentativa comum e retira a marca.
        var gateway = new GatewayOrdenado { Modo = GatewayOrdenado.ModoGateway.Falhar };
        var logs = new CapturaLogs();
        await using (var worker = WorkerEstornando(mensageria, stringConexao, logs, servicos: s => s.AddSingleton<IGatewayPagamento>(gateway)))
        {
            worker.CreateClient().Dispose();
            (await MensageriaTeste.EsperarAteAsync(async () => await TentativasAsync(stringConexao, falha.PedidoId) >= 1, Prazo)).Should().BeTrue();
        }

        var lido = await CorpoOkAsync(await ObterAsync(operador, falha.EstornoId));
        lido.GetProperty("tentativaForcadaEm").ValueKind.Should().Be(JsonValueKind.Null);
        lido.GetProperty("ultimoErro").GetString().Should().Be("Erro inesperado: TimeoutException.");
        logs.Registros.Should().Contain(r => r.Evento.Id == 7007);

        // US4 cenário 4: com o circuito aberto, nada é contado e a marca continua.
        var tentativasAntes = await TentativasAsync(stringConexao, adiado.PedidoId);
        await CorpoOkAsync(await ForcarAsync(operador, adiado.EstornoId), HttpStatusCode.Accepted);
        gateway.Modo = GatewayOrdenado.ModoGateway.CircuitoAberto;
        var logsAdiado = new CapturaLogs();
        await using (var worker = WorkerEstornando(mensageria, stringConexao, logsAdiado, servicos: s => s.AddSingleton<IGatewayPagamento>(gateway)))
        {
            worker.CreateClient().Dispose();
            (await EsperarLogsAsync(logsAdiado, 7020, 2)).Should().BeTrue("duas rodadas adiadas");
        }

        (await TentativasAsync(stringConexao, adiado.PedidoId)).Should().Be(tentativasAntes);
        (await CorpoOkAsync(await ObterAsync(operador, adiado.EstornoId))).GetProperty("tentativaForcadaEm").ValueKind
            .Should().Be(JsonValueKind.String, "a marca continua para quando o circuito fechar");
    }

    /// <summary>Registra a ordem das chamadas de estorno; confirma, falha ou finge o circuito aberto.</summary>
    private sealed class GatewayOrdenado : IGatewayPagamento
    {
        public enum ModoGateway
        {
            Confirmar,
            Falhar,
            CircuitoAberto
        }

        public volatile ModoGateway Modo = ModoGateway.Confirmar;

        public ConcurrentQueue<Guid> Chamadas { get; } = new();

        public Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken) =>
            Task.FromResult(ResultadoCobranca.Aprovada($"SIM-{pedidoId:N}"));

        public Task<ConsultaCobranca> ConsultarCobrancaAsync(Guid pedidoId, CancellationToken cancellationToken) =>
            Task.FromResult(ConsultaCobranca.NaoEncontrada);

        public Task<ResultadoEstorno> EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken cancellationToken) =>
            Modo switch
            {
                ModoGateway.CircuitoAberto => throw new GatewayPagamentoIndisponivelException(DateTimeOffset.UtcNow.AddMinutes(1)),
                ModoGateway.Falhar => Registrar(pedidoId, () => throw new TimeoutException("Gateway de pagamento sem resposta.")),
                _ => Registrar(pedidoId, () => Task.FromResult(ResultadoEstorno.Confirmar($"SIM-EST-{pedidoId:N}")))
            };

        private Task<ResultadoEstorno> Registrar(Guid pedidoId, Func<Task<ResultadoEstorno>> resultado)
        {
            Chamadas.Enqueue(pedidoId);
            return resultado();
        }
    }
}
