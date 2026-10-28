using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Entregas;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Administracao.CenarioReenvio;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Mensageria;

/// <summary>
/// Os consumidores voltam sozinhos depois de perder a inscrição, com o Worker no ar o tempo
/// todo (REQ-17; issue #41; contracts/recuperacao-consumo.md). O broker é parado e iniciado de
/// verdade (não só pausado), numa coleção própria.
/// </summary>
[Collection(nameof(ColecaoQuedaBroker))]
public sealed class RecuperacaoConsumoTestes(MensageriaTeste mensageria)
{
    /// <summary>FR-003: até 1 min depois de o broker voltar a aceitar conexões.</summary>
    private static readonly TimeSpan PrazoVolta = TimeSpan.FromSeconds(60);

    /// <summary>Pedido do começo ao fim depois da volta: até 1 min para voltar, mais o fluxo.</summary>
    private static readonly TimeSpan PrazoFluxo = TimeSpan.FromSeconds(90);

    private static readonly string[] Consumidores = ["estoque", "pagamento", "entrega"];

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    /// <summary>
    /// Reprodução da issue #41 (FR-001, T003; US1, cenários 1 e 2): um pedido criado com o broker
    /// fora deixa um evento pendente, e o despachante pede a conexão durante a queda.
    /// </summary>
    [Fact]
    public async Task Pedido_criado_com_o_broker_fora_segue_quando_ele_volta()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, stringConexao);

        await mensageria.PararBrokerAsync();
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        await Task.Delay(TimeSpan.FromSeconds(10), Cancelamento);
        await mensageria.IniciarBrokerAsync();

        (await UmConsumidorEmCadaFilaAsync(PrazoVolta)).Should().BeTrue("os três consumidores voltam sem reiniciar o Worker (FR-003)");
        (await ProntoParaEnvioAsync(stringConexao, pedidoId)).Should().BeTrue("o pedido segue o fluxo sem ação manual");
        (await CenarioEntrega.OrdensAsync(stringConexao, pedidoId)).Should().Be(1);
    }

    [Fact]
    public async Task Consumo_volta_depois_de_queda_longa()
    {
        // US1, cenário 4: 30 s parado já leva a espera ao teto de 10 s (analyze C1).
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, await mensageria.NovoBancoMigradoAsync());

        await mensageria.ReiniciarBrokerAsync(TimeSpan.FromSeconds(30));

        (await UmConsumidorEmCadaFilaAsync(PrazoVolta)).Should().BeTrue("o prazo conta a partir da volta do broker");
    }

    [Fact]
    public async Task Quedas_seguidas_deixam_um_unico_consumidor_por_fila()
    {
        // US1, cenário 5; FR-006.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, await mensageria.NovoBancoMigradoAsync());

        for (var queda = 0; queda < 3; queda++)
        {
            await mensageria.ReiniciarBrokerAsync(TimeSpan.FromSeconds(2));
        }

        (await UmConsumidorEmCadaFilaAsync(PrazoVolta)).Should().BeTrue();
        var limite = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < limite)
        {
            foreach (var consumidor in Consumidores)
            {
                (await mensageria.ConsumidoresNaFilaAsync(TopologiaMensageria.FilaPrincipal(consumidor))).Should().Be(1, "nenhuma inscrição duplicada em {0}", consumidor);
            }

            await Task.Delay(250, Cancelamento);
        }
    }

    [Fact]
    public async Task Broker_sem_dados_tem_as_filas_recriadas_antes_da_inscricao()
    {
        // US1, cenário 6; FR-005.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, await mensageria.NovoBancoMigradoAsync());

        await mensageria.RecriarBrokerAsync();

        (await UmConsumidorEmCadaFilaAsync(PrazoVolta)).Should().BeTrue();
        foreach (var consumidor in Consumidores)
        {
            foreach (var fila in new[] { 100, 200, 400 }.Select(ms => TopologiaMensageria.FilaEspera(consumidor, ms)).Append(TopologiaMensageria.FilaMortas(consumidor)))
            {
                (await mensageria.ConsumidoresNaFilaAsync(fila)).Should().NotBeNull("a fila {0} foi redeclarada", fila);
            }
        }
    }

    [Fact]
    public async Task Mensagem_em_processamento_na_queda_nao_repete_o_efeito()
    {
        // FR-008, FR-009, SC-003 (analyze U1): a reserva grava, segura antes de retornar, o broker
        // cai, o commit acontece com o canal fechado e o ack falha; a reentrega é duplicada.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var portao = new PortaoSalvar();
        using var logs = new CapturaLogs();
        await using var worker = await WorkerCompletoAsync(stringConexao, logs, servicos =>
            servicos.AddScoped<IArmazenamentoPedidos>(sp => new ArmazenamentoQueSegura(ActivatorUtilities.CreateInstance<ArmazenamentoPedidos>(sp), portao)));

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        await portao.Segurando.Task.WaitAsync(PrazoVolta, Cancelamento);
        await mensageria.PararBrokerAsync();
        portao.Liberar.TrySetResult();
        await Task.Delay(TimeSpan.FromSeconds(2), Cancelamento);
        await mensageria.IniciarBrokerAsync();

        (await ProntoParaEnvioAsync(stringConexao, pedidoId)).Should().BeTrue();
        (await EsperarLogAsync(logs, 5005, PrazoVolta)).Should().BeTrue("a reentrega do pedido.criado é descartada como duplicada");
        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(49, "uma reserva só");
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM EventosSaida WHERE Tipo = '{TiposEvento.EstoqueReservado}'")).Should().Be(1);
    }

    [Fact]
    public async Task Monitor_e_reenvio_da_dlq_funcionam_depois_da_queda()
    {
        // FR-011 (analyze A2). O despachante depois da queda está coberto pelo primeiro teste.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        await PrepararFilasAsync(mensageria);
        using var logs = new CapturaLogs();
        await using var worker = WorkerReenviando(mensageria, stringConexao, logs, new Dictionary<string, string?>
        {
            // Os mínimos das opções: avisa acima de 1 mensagem, lendo a cada 10 s.
            ["ReenvioDlq:AvisoHabilitado"] = "true",
            ["ReenvioDlq:AvisoLimite"] = "1",
            ["ReenvioDlq:AvisoIntervaloSegundos"] = "10"
        });
        worker.CreateClient().Dispose();

        await mensageria.ReiniciarBrokerAsync(TimeSpan.FromSeconds(5));
        var avisosAntes = logs.Registros.Count(r => r.Evento.Id == 5011);
        string[] ids = [await NaDlqAsync(mensageria), await NaDlqAsync(mensageria)];

        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(logs.Registros.Count(r => r.Evento.Id == 5011) > avisosAntes), PrazoVolta))
            .Should().BeTrue("o monitor volta a ler a DLQ");
        var pedido = await PedidoAsync(stringConexao);
        (await EsperarSituacaoAsync(stringConexao, pedido.Id, SituacaoReenvioDlq.Concluido)).Should().BeTrue("o reenvio volta a funcionar");
        (await EsvaziarAsync(mensageria, Principal)).Select(m => m.MessageId).Should().Equal(ids);
    }

    [Fact]
    public async Task Fila_apagada_e_recriada_e_o_consumidor_volta()
    {
        // US2, cenário 1: o broker cancela a inscrição, e o canal continua aberto.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        using var logs = new CapturaLogs();
        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, stringConexao, logs);

        await mensageria.ApagarFilasConsumidorAsync("estoque", soPrincipal: true);

        var outrasSempreInscritas = true;
        var voltou = await MensageriaTeste.EsperarAteAsync(async () =>
        {
            foreach (var outro in new[] { "pagamento", "entrega" })
            {
                outrasSempreInscritas &= await mensageria.ConsumidoresNaFilaAsync(TopologiaMensageria.FilaPrincipal(outro)) == 1;
            }

            return await mensageria.ConsumidoresNaFilaAsync(TopologiaMensageria.FilaPrincipal("estoque")) == 1;
        }, PrazoVolta);
        voltou.Should().BeTrue("a fila é recriada e o consumidor se inscreve de novo (FR-004, FR-005)");
        outrasSempreInscritas.Should().BeTrue("os outros consumidores não são afetados");
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await ProntoParaEnvioAsync(stringConexao, pedidoId)).Should().BeTrue();
        var perdas = logs.Registros.Where(r => r.Evento.Id == 5016).ToList();
        perdas.Should().ContainSingle().Which.Texto.Should().Contain("estoque").And.Contain("(cancelamento)");
        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 5018).Which.Texto.Should().Contain("estoque");
    }

    [Fact]
    public async Task Conexao_fechada_a_forca_tem_os_tres_consumidores_de_volta()
    {
        // US2, cenário 2.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var logs = new CapturaLogs();
        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, await mensageria.NovoBancoMigradoAsync(), logs);

        await mensageria.FecharConexoesAsync();

        (await EsperarLogAsync(logs, 5016, PrazoVolta)).Should().BeTrue();
        (await UmConsumidorEmCadaFilaAsync(PrazoVolta)).Should().BeTrue();
        logs.Registros.Should().Contain(r => r.Evento.Id == 5019, "a conexão registra o fechamento que o processo não pediu");
    }

    [Fact]
    public async Task Encerrar_o_worker_durante_a_recuperacao_nao_trava()
    {
        // US2, cenário 3.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var logs = new CapturaLogs();
        var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, await mensageria.NovoBancoMigradoAsync(), logs);
        try
        {
            await mensageria.PararBrokerAsync();
            (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(logs.Registros.Count(r => r.Evento.Id == 5016) >= 3), PrazoVolta)).Should().BeTrue();

            var relogio = System.Diagnostics.Stopwatch.StartNew();
            await worker.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), Cancelamento);
            var registrosNoFim = logs.Registros.Count;
            relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
            await Task.Delay(TimeSpan.FromSeconds(2), Cancelamento);
            logs.Registros.Should().HaveCount(registrosNoFim, "nada mais tenta se inscrever depois do encerramento");
            logs.Registros.Should().NotContain(r => r.Evento.Id == 5018);
        }
        finally
        {
            await mensageria.IniciarBrokerAsync();
        }
    }

    [Fact]
    public async Task Queda_e_volta_aparecem_na_prontidao_e_nos_logs_sem_credenciais()
    {
        // US3, cenários 1 a 4; SC-004; FR-015, FR-016 (analyze C1: uma queda só).
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var logs = new CapturaLogs();
        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, await mensageria.NovoBancoMigradoAsync(), logs);
        var http = worker.CreateClient();
        (await MensageriaTeste.EsperarAteAsync(async () => (await ProntidaoAsync(http)).Codigo == HttpStatusCode.OK, PrazoVolta)).Should().BeTrue();
        (await ProntidaoAsync(http)).Consumidores.Should().Be("Healthy");

        await mensageria.PararBrokerAsync();
        (await MensageriaTeste.EsperarAteAsync(async () => (await ProntidaoAsync(http)).Consumidores == "Unhealthy", TimeSpan.FromSeconds(15))).Should().BeTrue();
        for (var leitura = 0; leitura < 5; leitura++)
        {
            var prontidao = await ProntidaoAsync(http);
            prontidao.Codigo.Should().Be(HttpStatusCode.ServiceUnavailable, "toda leitura até a volta é não pronta");
            prontidao.Corpo.Should().NotContain(MensageriaTeste.SenhaBroker).And.NotContain("oms.estoque").And.NotContain(mensageria.ConfiguracoesBroker["RabbitMQ:Servidor"]!);
            await Task.Delay(200, Cancelamento);
        }

        await mensageria.IniciarBrokerAsync();

        (await MensageriaTeste.EsperarAteAsync(async () => (await ProntidaoAsync(http)).Codigo == HttpStatusCode.OK, PrazoVolta)).Should().BeTrue("volta a pronta em até 1 min");
        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(Consumidores.All(c => logs.Registros.Any(r => r.Evento.Id == 5018 && r.Texto.Contains(c, StringComparison.Ordinal)))), PrazoVolta))
            .Should().BeTrue();
        var registros = logs.Registros.ToList();
        registros.Should().Contain(r => r.Evento.Id == 5019 && r.Nivel == LogLevel.Warning);
        foreach (var consumidor in Consumidores)
        {
            registros.Should().ContainSingle(r => r.Evento.Id == 5016 && r.Texto.Contains($"Consumidor {consumidor} ", StringComparison.Ordinal))
                .Which.Texto.Should().Contain("(canal)");
            registros.Should().Contain(r => r.Evento.Id == 5017 && r.Nivel == LogLevel.Warning && r.Texto.Contains($"Consumidor {consumidor} ", StringComparison.Ordinal));
            var volta = registros.Should().ContainSingle(r => r.Evento.Id == 5018 && r.Texto.Contains($"Consumidor {consumidor} ", StringComparison.Ordinal)).Subject;
            volta.Nivel.Should().Be(LogLevel.Information);
            volta.Texto.Should().NotContain("depois de 0 ms");
        }

        registros.Should().NotContain(r => r.Texto.Contains(MensageriaTeste.SenhaBroker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Credenciais_recusadas_mantem_o_worker_nao_pronto_ate_voltarem()
    {
        // Spec, Edge Cases (analyze G1).
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        const string OutraSenha = "outra-senha-req17";
        using var logs = new CapturaLogs();
        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, await mensageria.NovoBancoMigradoAsync(), logs);
        var http = worker.CreateClient();
        try
        {
            await mensageria.TrocarSenhaAsync(OutraSenha);
            await mensageria.FecharConexoesAsync();

            (await EsperarLogAsync(logs, 5017, PrazoVolta)).Should().BeTrue("as tentativas falham com a senha recusada");
            var limite = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < limite)
            {
                var prontidao = await ProntidaoAsync(http);
                prontidao.Codigo.Should().Be(HttpStatusCode.ServiceUnavailable);
                prontidao.Consumidores.Should().Be("Unhealthy");
                await Task.Delay(500, Cancelamento);
            }
        }
        finally
        {
            await mensageria.RestaurarSenhaAsync();
        }

        (await UmConsumidorEmCadaFilaAsync(PrazoVolta)).Should().BeTrue("com a senha de volta, os consumidores voltam");
        logs.Registros.Should().NotContain(r => r.Texto.Contains(OutraSenha, StringComparison.Ordinal) || r.Texto.Contains(MensageriaTeste.SenhaBroker, StringComparison.Ordinal));
    }

    /// <summary>Espera cada fila principal ter exatamente um consumidor, até o prazo.</summary>
    private async Task<bool> UmConsumidorEmCadaFilaAsync(TimeSpan prazo) =>
        await MensageriaTeste.EsperarAteAsync(async () =>
        {
            foreach (var consumidor in Consumidores)
            {
                if (await mensageria.ConsumidoresNaFilaAsync(TopologiaMensageria.FilaPrincipal(consumidor)) != 1)
                {
                    return false;
                }
            }

            return true;
        }, prazo);

    private static Task<bool> ProntoParaEnvioAsync(string stringConexao, Guid pedidoId) =>
        MensageriaTeste.EsperarAteAsync(async () => await StatusAsync(stringConexao, pedidoId) == "ProntoParaEnvio", PrazoFluxo);

    private static async Task<(HttpStatusCode Codigo, string Corpo, string? Consumidores)> ProntidaoAsync(HttpClient http)
    {
        var resposta = await http.GetAsync("/health/pronto", Cancelamento);
        var corpo = await resposta.Content.ReadAsStringAsync(Cancelamento);
        using var json = JsonDocument.Parse(corpo);
        var consumidores = json.RootElement.GetProperty("dependencias").TryGetProperty("consumidores", out var valor) ? valor.GetString() : null;
        return (resposta.StatusCode, corpo, consumidores);
    }

    /// <summary>Como <see cref="CenarioEntrega.WorkerProntoAsync"/>, com serviços extras.</summary>
    private async Task<FabricaWorker> WorkerCompletoAsync(string stringConexao, CapturaLogs logs, Action<IServiceCollection> servicos)
    {
        foreach (var consumidor in Consumidores)
        {
            await mensageria.LimparFilasConsumidorAsync(consumidor);
        }

        var worker = new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
            Servicos = colecao =>
            {
                colecao.AddSingleton<ILoggerProvider>(logs);
                servicos(colecao);
            }
        };
        worker.CreateClient().Dispose();
        (await UmConsumidorEmCadaFilaAsync(TimeSpan.FromSeconds(10))).Should().BeTrue("os três consumidores precisam estar prontos");
        return worker;
    }

    /// <summary>Segura a primeira gravação até o teste liberar.</summary>
    private sealed class PortaoSalvar
    {
        private int usado;

        public TaskCompletionSource Segurando { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Primeira() => Interlocked.Exchange(ref usado, 1) == 0;
    }

    /// <summary>
    /// O armazenamento real; a primeira gravação (a da reserva) acontece e segura antes de voltar
    /// ao <c>ConsumidorEventos</c>, que só confirma a transação depois (analyze U1).
    /// </summary>
    private sealed class ArmazenamentoQueSegura(IArmazenamentoPedidos real, PortaoSalvar portao) : IArmazenamentoPedidos
    {
        public Task<IReadOnlyList<Produto>> ObterProdutosAsync(IReadOnlyCollection<Guid> ids, bool paraAlterar, CancellationToken cancellationToken) =>
            real.ObterProdutosAsync(ids, paraAlterar, cancellationToken);

        public Task<Pedido?> ObterPedidoAsync(Guid id, bool paraAlterar, CancellationToken cancellationToken) =>
            real.ObterPedidoAsync(id, paraAlterar, cancellationToken);

        public Task<bool> RastreioEmUsoAsync(string transportadora, string codigoRastreio, Guid pedidoId, CancellationToken cancellationToken) =>
            real.RastreioEmUsoAsync(transportadora, codigoRastreio, pedidoId, cancellationToken);

        public Task<RegistroIdempotencia?> ObterRegistroIdempotenciaAsync(Guid clienteId, Guid chave, CancellationToken cancellationToken) =>
            real.ObterRegistroIdempotenciaAsync(clienteId, chave, cancellationToken);

        public void Adicionar(Pedido pedido, RegistroIdempotencia registro, EventoSaida evento) => real.Adicionar(pedido, registro, evento);

        public void Adicionar(EventoSaida evento) => real.Adicionar(evento);

        public async Task SalvarAsync(CancellationToken cancellationToken)
        {
            await real.SalvarAsync(cancellationToken);
            if (portao.Primeira())
            {
                portao.Segurando.TrySetResult();
                await portao.Liberar.Task;
            }
        }

        public void DescartarAlteracoes() => real.DescartarAlteracoes();
    }
}
