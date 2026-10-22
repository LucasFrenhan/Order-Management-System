using System.Collections.Concurrent;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using RabbitMQ.Client;

namespace PedidoFulfillment.Testes.Integracao.Mensageria;

/// <summary>
/// User Story 3 com RabbitMQ e SQL Server reais (REQ-06, FR-012 a FR-016, SC-004, SC-005): o
/// manipulador de verificação conta as execuções de cada evento. Novas tentativas a 100, 200 e
/// 400 ms (<see cref="FabricaWorker"/>).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class ConsumoComNovaTentativaTestes(MensageriaTeste mensageria)
{
    private const string Consumidor = "verificacao";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private async Task<(FabricaWorker Worker, ControleVerificacao Controle)> WorkerAsync(string stringConexao, CapturaLogs? logs = null)
    {
        var controle = new ControleVerificacao();
        var worker = new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
            Servicos = servicos =>
            {
                servicos.AddSingleton(controle);
                servicos.AdicionarConsumidor<ManipuladorVerificacao, PedidoCriado>();
                if (logs is not null)
                {
                    servicos.AddLogging(l => l.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
                }
            }
        };
        worker.CreateClient().Dispose();
        await EsperarConsumidorAsync();
        return (worker, controle);
    }

    /// <summary>A fila só existe e tem consumidor depois que o Worker declara a topologia.</summary>
    private async Task EsperarConsumidorAsync()
    {
        await using var conexao = await mensageria.ConectarAsync();
        (await MensageriaTeste.EsperarAteAsync(async () =>
        {
            try
            {
                await using var canal = await conexao.CreateChannelAsync(cancellationToken: Cancelamento);
                return (await canal.QueueDeclarePassiveAsync(TopologiaMensageria.FilaPrincipal(Consumidor), Cancelamento)).ConsumerCount > 0;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                return false;
            }
        }, TimeSpan.FromSeconds(10))).Should().BeTrue("o consumidor precisa estar pronto");
    }

    private async Task PublicarAsync(string corpo, string? idEvento = null)
    {
        await using var conexao = await mensageria.ConectarAsync();
        await using var canal = await conexao.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), Cancelamento);
        var propriedades = new BasicProperties { Persistent = true, MessageId = idEvento, Type = TiposEvento.PedidoCriado };
        await canal.BasicPublishAsync(TopologiaMensageria.TrocaEventos, TiposEvento.PedidoCriado, false, propriedades, Encoding.UTF8.GetBytes(corpo), Cancelamento);
    }

    private static EnvelopeEvento<PedidoCriado> Evento() => EnvelopeEvento.Criar(Guid.CreateVersion7(), new PedidoCriado
    {
        PedidoId = Guid.CreateVersion7(),
        Numero = "PED-20261022-000001",
        ClienteId = Guid.CreateVersion7(),
        Itens = [new ItemPedidoCriado { ProdutoId = Guid.CreateVersion7(), Quantidade = 1, PrecoUnitario = 10m }],
        ValorTotal = 10m,
        Moeda = "BRL"
    }, DateTimeOffset.UtcNow);

    private Task PublicarAsync(EnvelopeEvento<PedidoCriado> evento) =>
        PublicarAsync(SerializacaoEventos.Serializar(evento), evento.IdEvento.ToString());

    private async Task<List<BasicGetResult>> LerDlqAsync()
    {
        await using var conexao = await mensageria.ConectarAsync();
        await using var canal = await conexao.CreateChannelAsync(cancellationToken: Cancelamento);
        var mensagens = new List<BasicGetResult>();
        while (await canal.BasicGetAsync(TopologiaMensageria.FilaMortas(Consumidor), autoAck: true, Cancelamento) is { } mensagem)
        {
            mensagens.Add(mensagem);
        }

        return mensagens;
    }

    private static string Cabecalho(BasicGetResult mensagem, string nome) => mensagem.BasicProperties.Headers![nome] switch
    {
        byte[] texto => Encoding.UTF8.GetString(texto),
        var valor => Convert.ToString(valor, System.Globalization.CultureInfo.InvariantCulture)!
    };

    [Fact]
    public async Task Falha_temporaria_e_processada_na_terceira_execucao_com_espera_crescente()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (worker, controle) = await WorkerAsync(await mensageria.NovoBancoMigradoAsync());
        await using var _ = worker;
        var evento = Evento();
        controle.FalharVezes[evento.IdEvento] = 2;

        await PublicarAsync(evento);

        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(controle.Processados.ContainsKey(evento.IdEvento)), TimeSpan.FromSeconds(10))).Should().BeTrue();
        var execucoes = controle.Execucoes[evento.IdEvento];
        execucoes.Should().HaveCount(3);
        (execucoes[1] - execucoes[0]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(90));
        (execucoes[2] - execucoes[1]).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(180));
        (await LerDlqAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Falha_permanente_vai_para_a_DLQ_depois_de_4_execucoes()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        var (worker, controle) = await WorkerAsync(await mensageria.NovoBancoMigradoAsync(), logs);
        await using var _ = worker;
        var evento = Evento();
        controle.FalharVezes[evento.IdEvento] = int.MaxValue;

        await PublicarAsync(evento);

        List<BasicGetResult> dlq = [];
        (await MensageriaTeste.EsperarAteAsync(async () => (dlq = await LerDlqAsync()).Count > 0, TimeSpan.FromSeconds(10))).Should().BeTrue();
        controle.Execucoes[evento.IdEvento].Should().HaveCount(4);
        var morta = dlq.Single();
        Cabecalho(morta, PoliticaNovaTentativa.CabecalhoTentativas).Should().Be("3");
        Cabecalho(morta, PoliticaNovaTentativa.CabecalhoMotivo).Should().Contain("falha de verificação");
        Cabecalho(morta, PoliticaNovaTentativa.CabecalhoFalhouEm).Should().NotBeNullOrEmpty();
        SerializacaoEventos.Desserializar<PedidoCriado>(Encoding.UTF8.GetString(morta.Body.Span)).IdEvento.Should().Be(evento.IdEvento);

        // FR-020: os logs citam o evento, a fila e a tentativa, sem dados pessoais.
        logs.Registros.Select(r => r.Evento.Name).Should().Contain(["NovaTentativa", "EnviadaParaDlq"]);
        logs.Textos.Where(t => t.Contains(evento.IdEvento.ToString(), StringComparison.Ordinal)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task Mensagem_fora_do_contrato_vai_direto_para_a_DLQ()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (worker, controle) = await WorkerAsync(await mensageria.NovoBancoMigradoAsync());
        await using var _ = worker;

        await PublicarAsync("""{"tipo":"pedido.criado","versao":1,"dados":{}}""", "invalida-1");

        List<BasicGetResult> dlq = [];
        (await MensageriaTeste.EsperarAteAsync(async () => (dlq = await LerDlqAsync()).Count > 0, TimeSpan.FromSeconds(10))).Should().BeTrue();
        controle.Execucoes.Should().BeEmpty();
        Cabecalho(dlq.Single(), PoliticaNovaTentativa.CabecalhoTentativas).Should().Be("0");
        Cabecalho(dlq.Single(), PoliticaNovaTentativa.CabecalhoMotivo).Should().StartWith("Conteúdo fora do contrato");
    }

    [Fact]
    public async Task Evento_entregue_duas_vezes_produz_o_efeito_uma_vez()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        var (worker, controle) = await WorkerAsync(stringConexao, logs);
        await using var _ = worker;
        var evento = Evento();

        await PublicarAsync(evento);
        await PublicarAsync(evento);

        (await MensageriaTeste.EsperarAteAsync(
            () => Task.FromResult(logs.Registros.Any(r => r.Evento.Name == "DuplicadoDescartado")), TimeSpan.FromSeconds(10))).Should().BeTrue();
        controle.Execucoes[evento.IdEvento].Should().HaveCount(1);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM EventosProcessados WHERE Consumidor = '{Consumidor}' AND IdEvento = '{evento.IdEvento}'")).Should().Be(1);
    }

    [Fact]
    public async Task Mensagens_publicadas_com_o_worker_parado_sao_processadas_quando_ele_volta()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        var (primeiro, _) = await WorkerAsync(stringConexao);
        await primeiro.DisposeAsync();
        var eventos = Enumerable.Range(0, 3).Select(_ => Evento()).ToList();
        foreach (var evento in eventos)
        {
            await PublicarAsync(evento);
        }

        var (segundo, controle) = await WorkerAsync(stringConexao);
        await using var _ = segundo;

        (await MensageriaTeste.EsperarAteAsync(
            () => Task.FromResult(eventos.All(e => controle.Processados.ContainsKey(e.IdEvento))), TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task Espera_de_nova_tentativa_nao_bloqueia_as_demais_mensagens()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (worker, controle) = await WorkerAsync(await mensageria.NovoBancoMigradoAsync());
        await using var _ = worker;
        var lenta = Evento();
        var rapida = Evento();
        controle.FalharVezes[lenta.IdEvento] = 1;

        await PublicarAsync(lenta);
        await PublicarAsync(rapida);

        (await MensageriaTeste.EsperarAteAsync(
            () => Task.FromResult(controle.Processados.ContainsKey(lenta.IdEvento) && controle.Processados.ContainsKey(rapida.IdEvento)), TimeSpan.FromSeconds(10)))
            .Should().BeTrue();
        controle.Processados[rapida.IdEvento].Should().BeBefore(controle.Execucoes[lenta.IdEvento][1]);
    }

    [Fact]
    public async Task Falha_do_manipulador_desfaz_o_que_ele_gravou()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        var (worker, controle) = await WorkerAsync(stringConexao);
        await using var _ = worker;
        var evento = Evento();
        controle.FalharVezes[evento.IdEvento] = int.MaxValue;
        controle.GravarProdutoAntesDeFalhar = true;

        await PublicarAsync(evento);

        (await MensageriaTeste.EsperarAteAsync(async () => (await LerDlqAsync()).Count > 0, TimeSpan.FromSeconds(10))).Should().BeTrue();
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM Produtos WHERE Sku LIKE 'VERIF-%'")).Should().Be(0);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM EventosProcessados")).Should().Be(0);
    }
}

/// <summary>Estado compartilhado do manipulador de verificação.</summary>
public sealed class ControleVerificacao
{
    /// <summary>Quantas vezes falhar antes de processar, por evento (<see cref="int.MaxValue"/> = sempre).</summary>
    public ConcurrentDictionary<Guid, int> FalharVezes { get; } = new();

    public ConcurrentDictionary<Guid, List<DateTimeOffset>> Execucoes { get; } = new();

    public ConcurrentDictionary<Guid, DateTimeOffset> Processados { get; } = new();

    public bool GravarProdutoAntesDeFalhar { get; set; }
}

/// <summary>Manipulador usado só nos testes da base de consumo.</summary>
public sealed class ManipuladorVerificacao(ControleVerificacao controle, ContextoBancoDados contexto) : IManipuladorEvento<PedidoCriado>
{
    public string Consumidor => "verificacao";

    public IReadOnlyCollection<string> Tipos => [TiposEvento.PedidoCriado];

    public async Task ManipularAsync(EnvelopeEvento<PedidoCriado> evento, CancellationToken cancellationToken)
    {
        var execucoes = controle.Execucoes.GetOrAdd(evento.IdEvento, _ => []);
        lock (execucoes)
        {
            execucoes.Add(DateTimeOffset.UtcNow);
        }

        if (execucoes.Count <= controle.FalharVezes.GetValueOrDefault(evento.IdEvento))
        {
            if (controle.GravarProdutoAntesDeFalhar)
            {
                contexto.Produtos.Add(new Produto($"VERIF-{Guid.NewGuid():N}"[..20], "Verificação", null, 1m, 1, true, DateTimeOffset.UtcNow));
                await contexto.SaveChangesAsync(cancellationToken);
            }

            throw new InvalidOperationException("falha de verificação");
        }

        controle.Processados[evento.IdEvento] = DateTimeOffset.UtcNow;
    }
}
