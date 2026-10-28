using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Infrastructure.Observabilidade;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Administracao.CenarioReenvio;

namespace PedidoFulfillment.Testes.Integracao.Observabilidade;

/// <summary>
/// User Story 5 do REQ-16 (T026 e T027): gauges do outbox e da DLQ e o ritmo do aviso 5011 com a
/// telemetria ligada. Os contadores são conferidos nos testes de trace, que já montam os cenários.
/// Na coleção sem paralelismo (analyze U1), por isso fora de <c>MonitorDlqTestes</c>.
/// </summary>
[Collection(nameof(ColecaoObservabilidade))]
public sealed class MetricasTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Gauges_do_outbox_mostram_os_pendentes_e_a_idade_do_mais_antigo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        var criadoEm = DateTimeOffset.UtcNow.AddMinutes(-2);
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            for (var i = 0; i < 2; i++)
            {
                contexto.EventosSaida.Add(EventoSaida.De(EnvelopeEvento.Criar(Guid.CreateVersion7(), new PedidoCriado
                {
                    PedidoId = Guid.CreateVersion7(),
                    Numero = "PED-20261026-000002",
                    ClienteId = Guid.CreateVersion7(),
                    ValorTotal = 10m,
                    Moeda = "BRL",
                    Itens = [new ItemPedidoCriado { ProdutoId = Guid.CreateVersion7(), Quantidade = 1, PrecoUnitario = 10m }]
                }, criadoEm), criadoEm));
            }

            await contexto.SaveChangesAsync(Cancelamento);
        }

        using var coletor = new ColetorTelemetria();
        // Broker inacessível: o despachante não esvazia o outbox durante o teste.
        await using var worker = new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
            Configuracoes = new Dictionary<string, string?> { ["RabbitMQ:Porta"] = "1" },
            Telemetria = coletor
        };
        worker.CreateClient().Dispose();
        var amostrador = worker.Services.GetServices<IHostedService>().OfType<AmostradorOutbox>().Single();

        await amostrador.AmostrarAsync(Cancelamento);
        coletor.LerGauges();
        coletor.Ultimo("oms.outbox.eventos_pendentes").Should().Be(2);
        coletor.Ultimo("oms.outbox.pendente_mais_antigo.idade").Should().BeGreaterThanOrEqualTo(120);

        await BancoDadosTeste.ExecutarAsync(stringConexao, "UPDATE EventosSaida SET EnviadoEm = SYSDATETIMEOFFSET()");
        await amostrador.AmostrarAsync(Cancelamento);
        coletor.LerGauges();
        coletor.Ultimo("oms.outbox.eventos_pendentes").Should().Be(0);
        coletor.Ultimo("oms.outbox.pendente_mais_antigo.idade").Should().Be(0, "sem pendentes, a idade é zero");
    }

    [Fact]
    public async Task Gauge_da_dlq_por_consumidor_e_o_aviso_5011_no_ritmo_de_antes()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        await PrepararFilasAsync(mensageria);
        for (var i = 0; i < 3; i++)
        {
            await NaDlqAsync(mensageria);
        }

        using var coletor = new ColetorTelemetria();
        var logs = new CapturaLogs();
        await using var worker = new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
            Telemetria = coletor,
            Servicos = colecao => colecao.AddSingleton<ILoggerProvider>(logs)
        };
        worker.CreateClient().Dispose();
        var monitor = worker.Services.GetServices<IHostedService>().OfType<MonitorDlq>().Single();

        // Leituras seguidas, como a de 1 min da métrica: o aviso sai uma vez só no intervalo dele.
        await monitor.VerificarAsync(new OpcoesReenvioDlq { AvisoLimite = 2 }, Cancelamento);
        await monitor.VerificarAsync(new OpcoesReenvioDlq { AvisoLimite = 2 }, Cancelamento);
        logs.Registros.Where(r => r.Evento.Id == 5011).Should().ContainSingle("o 5011 sai no máximo uma vez por AvisoIntervaloSegundos (FR-018)");

        coletor.LerGauges();
        coletor.Ultimo("oms.mensageria.dlq.mensagens", ("oms.consumidor", Consumidor)).Should().Be(3);

        // Aviso desligado, telemetria ligada: lê para a métrica sem avisar.
        await NaDlqAsync(mensageria);
        await monitor.VerificarAsync(new OpcoesReenvioDlq { AvisoHabilitado = false, AvisoLimite = 1 }, Cancelamento);
        coletor.LerGauges();
        coletor.Ultimo("oms.mensageria.dlq.mensagens", ("oms.consumidor", Consumidor)).Should().Be(4);
        logs.Registros.Where(r => r.Evento.Id == 5011).Should().ContainSingle();
        await PrepararFilasAsync(mensageria);
    }

    [Fact]
    public async Task Inscricao_dos_consumidores_e_reinscricoes_aparecem_nas_metricas()
    {
        // REQ-17, FR-016a (métricas 7 e 8): a fila apagada derruba só a inscrição do estoque.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var coletor = new ColetorTelemetria();
        await using var worker = new FabricaWorker
        {
            StringConexao = await mensageria.NovoBancoMigradoAsync(),
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
            Telemetria = coletor
        };
        worker.CreateClient().Dispose();
        foreach (var consumidor in new[] { "estoque", "pagamento", "entrega" })
        {
            (await mensageria.ConsumidorLigadoAsync(consumidor, TimeSpan.FromSeconds(10))).Should().BeTrue();
        }

        coletor.LerGauges();
        foreach (var consumidor in new[] { "estoque", "pagamento", "entrega" })
        {
            coletor.Ultimo("oms.mensageria.consumidor.inscrito", ("oms.consumidor", consumidor)).Should().Be(1);
        }

        coletor.Soma("oms.mensageria.consumidor.reinscricoes").Should().Be(0, "a inscrição da partida não conta");

        await mensageria.ApagarFilasConsumidorAsync("estoque", soPrincipal: true);
        // A volta é imediata depois de a fila sumir: o 0 do gauge dura milissegundos e fica com o
        // teste unitário do EstadoConsumidores.
        (await MensageriaTeste.EsperarAteAsync(
            () => Task.FromResult(coletor.Soma("oms.mensageria.consumidor.reinscricoes", ("oms.consumidor", "estoque")) == 1),
            TimeSpan.FromSeconds(60))).Should().BeTrue("o consumidor voltou e a volta foi contada");

        coletor.LerGauges();
        coletor.Ultimo("oms.mensageria.consumidor.inscrito", ("oms.consumidor", "estoque")).Should().Be(1);
        coletor.Soma("oms.mensageria.consumidor.reinscricoes", ("oms.consumidor", "pagamento")).Should().Be(0);
        var inscrito = coletor.Medicoes.First(m => m.Nome == "oms.mensageria.consumidor.inscrito");
        inscrito.Unidade.Should().Be("{inscricao}");
        coletor.Medicoes.First(m => m.Nome == "oms.mensageria.consumidor.reinscricoes").Unidade.Should().Be("{reinscricao}");
    }
}
