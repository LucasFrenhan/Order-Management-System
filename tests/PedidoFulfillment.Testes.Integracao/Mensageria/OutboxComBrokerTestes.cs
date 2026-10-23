using System.Net;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Mensageria;

/// <summary>
/// User Story 1 com SQL Server e RabbitMQ reais: a criação grava <c>pedido.criado</c> no outbox,
/// e o Worker publica com confirmação, aguentando broker e Worker fora (REQ-06, FR-005 a FR-009).
/// A API sem o Worker continua criando pedidos (US4, cenário 2): coberto em
/// <see cref="Worker_parado_deixa_os_eventos_pendentes_ate_voltar"/>.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class OutboxComBrokerTestes(MensageriaTeste mensageria)
{
    // Só pedido.criado: com o consumidor de estoque (REQ-08) no Worker, estoque.* também entra no outbox.
    private const string ContarPendentes = "SELECT COUNT(*) FROM EventosSaida WHERE EnviadoEm IS NULL AND Tipo = 'pedido.criado'";

    // A API não fala com o broker (clarify): só grava o evento no outbox.
    private static FabricaApi ApiComBroker(string stringConexao) => Api(stringConexao);

    private FabricaWorker Worker(string stringConexao) => new()
    {
        StringConexao = stringConexao,
        ConfiguracoesBroker = mensageria.ConfiguracoesBroker
    };

    private static async Task<HashSet<Guid>> IdsRecebidosAsync(FilaTeste fila)
    {
        var ids = new HashSet<Guid>();
        foreach (var mensagem in fila.Mensagens)
        {
            ids.Add(SerializacaoEventos.Desserializar<PedidoCriado>(mensagem.Texto).IdEvento);
        }

        return await Task.FromResult(ids);
    }

    [Fact]
    public async Task Pedido_criado_e_publicado_em_ate_5_segundos()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = ApiComBroker(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync(TiposEvento.PedidoCriado);
        await using var worker = Worker(stringConexao);
        worker.CreateClient().Dispose();

        var criado = await CriadoAsync(cliente, (Cat001, 2), (Cat002, 1));

        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(!fila.Mensagens.IsEmpty), TimeSpan.FromSeconds(5))).Should().BeTrue();
        var mensagem = fila.Mensagens.Single();
        mensagem.Propriedades.Persistent.Should().BeTrue();
        mensagem.Propriedades.Type.Should().Be(TiposEvento.PedidoCriado);
        mensagem.Propriedades.ContentType.Should().Be("application/json");
        var envelope = SerializacaoEventos.Desserializar<PedidoCriado>(mensagem.Texto);
        mensagem.Propriedades.MessageId.Should().Be(envelope.IdEvento.ToString());
        envelope.Dados.PedidoId.Should().Be(criado.GetProperty("id").GetGuid());
        envelope.Dados.Numero.Should().Be(criado.GetProperty("numero").GetString());
        envelope.Dados.ValorTotal.Should().Be(829.70m);
        envelope.Dados.Itens.Should().HaveCount(2);
        (await MensageriaTeste.EsperarAteAsync(
            async () => await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarPendentes) == 0, TimeSpan.FromSeconds(5))).Should().BeTrue();
    }

    [Fact]
    public async Task Com_o_broker_fora_os_pedidos_sao_criados_e_os_eventos_saem_quando_ele_volta()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = ApiComBroker(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync(TiposEvento.PedidoCriado);
        await using var worker = Worker(stringConexao);
        worker.CreateClient().Dispose();

        await mensageria.PausarBrokerAsync();
        try
        {
            for (var i = 0; i < 20; i++)
            {
                (await CriarAsync(cliente, Guid.NewGuid(), (Cat001, 1))).StatusCode.Should().Be(HttpStatusCode.Created);
            }
        }
        finally
        {
            await mensageria.RetomarBrokerAsync();
        }

        // SC-003: os 20 em até 30 s depois que o broker volta; duplicados são aceitos (pelo menos uma vez).
        (await MensageriaTeste.EsperarAteAsync(async () => (await IdsRecebidosAsync(fila)).Count >= 20, TimeSpan.FromSeconds(30)))
            .Should().BeTrue();
        (await IdsRecebidosAsync(fila)).Should().HaveCount(20);
    }

    [Fact]
    public async Task Falha_de_produto_e_repeticao_nao_gravam_evento()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = ApiComBroker(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var chave = Guid.NewGuid();

        (await CriarAsync(cliente, Guid.NewGuid(), (Cat009, 1))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CriarAsync(cliente, chave, (Cat001, 1))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CriarAsync(cliente, chave, (Cat001, 1))).StatusCode.Should().Be(HttpStatusCode.Created);

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM EventosSaida WHERE Tipo = 'pedido.criado'")).Should().Be(1);
    }

    [Fact]
    public async Task Worker_parado_deixa_os_eventos_pendentes_ate_voltar()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = ApiComBroker(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync(TiposEvento.PedidoCriado);

        await CriadoAsync(cliente, (Cat001, 1));
        await CriadoAsync(cliente, (Cat002, 1));
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarPendentes)).Should().Be(2);

        await using var worker = Worker(stringConexao);
        worker.CreateClient().Dispose();

        (await MensageriaTeste.EsperarAteAsync(async () => (await IdsRecebidosAsync(fila)).Count == 2, TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task Dois_workers_juntos_publicam_todos_os_pendentes()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = ApiComBroker(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync(TiposEvento.PedidoCriado);
        for (var i = 0; i < 30; i++)
        {
            await CriadoAsync(cliente, (Cat001, 1));
        }

        await using var primeiro = Worker(stringConexao);
        await using var segundo = Worker(stringConexao);
        await Task.WhenAll(
            Task.Run(() => primeiro.CreateClient().Dispose(), TestContext.Current.CancellationToken),
            Task.Run(() => segundo.CreateClient().Dispose(), TestContext.Current.CancellationToken));

        (await MensageriaTeste.EsperarAteAsync(async () => (await IdsRecebidosAsync(fila)).Count == 30, TimeSpan.FromSeconds(15))).Should().BeTrue();
        // O commit vem depois do lote: as mensagens chegam antes de os pendentes zerarem.
        (await MensageriaTeste.EsperarAteAsync(
            async () => await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarPendentes) == 0, TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task Corpo_publicado_e_json_do_envelope()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = ApiComBroker(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync(TiposEvento.PedidoCriado);
        await using var worker = Worker(stringConexao);
        worker.CreateClient().Dispose();

        await CriadoAsync(cliente, (Cat001, 1));

        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(!fila.Mensagens.IsEmpty), TimeSpan.FromSeconds(5))).Should().BeTrue();
        using var json = JsonDocument.Parse(fila.Mensagens.Single().Corpo);
        json.RootElement.GetProperty("tipo").GetString().Should().Be("pedido.criado");
        json.RootElement.GetProperty("versao").GetInt32().Should().Be(1);
    }
}
