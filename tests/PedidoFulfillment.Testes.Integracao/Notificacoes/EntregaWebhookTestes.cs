using FluentAssertions;
using PedidoFulfillment.Infrastructure.Notificacoes;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Mensageria;
using static PedidoFulfillment.Testes.Integracao.Notificacoes.CenarioWebhooks;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Notificacoes;

/// <summary>
/// Entrega dos webhooks com API, Worker, banco e receptor reais (REQ-18, US2 cenários 2 a 8;
/// FR-012 a FR-017; analyze U2).
/// </summary>
[Collection(nameof(ColecaoMensageriaEntregas))]
public sealed class EntregaWebhookTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Aviso_do_pedido_do_Cliente_chega_assinado_ao_destino_dele_e_o_global_recebe_todos()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (dono, _, _) = await ClienteAsync(api);
        var (outro, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var doDono = await ReceptorWebhookTeste.IniciarAsync();
        await using var doGlobal = await ReceptorWebhookTeste.IniciarAsync();
        var (destinoId, segredo) = await DestinoAsync(dono, doDono.Url());
        await DestinoAsync(await AdminAsync(api), doGlobal.Url());
        var pedidoDono = await PedidoComAvisoAsync(dono, operador);
        await PedidoComAvisoAsync(outro, operador);

        await using var worker = WorkerEnviando(mensageria, stringConexao);
        worker.CreateClient().Dispose();

        (await EsperarRecebidasAsync(doDono, 1)).Should().BeTrue();
        (await EsperarRecebidasAsync(doGlobal, 2)).Should().BeTrue("o destino global recebe os avisos de todos os pedidos");
        (await EsperarSituacaoAsync(stringConexao, destinoId, "Entregue")).Should().BeTrue();
        await Task.Delay(TimeSpan.FromSeconds(2), Token);
        var requisicao = doDono.Recebidas.Should().ContainSingle("o pedido do outro Cliente não chega ao destino do dono").Subject;

        var id = requisicao.Cabecalhos[AssinaturaWebhook.CabecalhoId];
        var timestamp = requisicao.Cabecalhos[AssinaturaWebhook.CabecalhoTimestamp];
        requisicao.Cabecalhos[AssinaturaWebhook.CabecalhoAssinatura].Should().Be(AssinaturaWebhook.Assinar(segredo, id, timestamp, requisicao.Corpo));
        requisicao.Cabecalhos["content-type"].Should().StartWith("application/json");
        requisicao.Cabecalhos.Should().NotContainKey("traceparent");
        Guid.Parse(id).Should().Be(IdNotificacao(requisicao));
        var conteudo = await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT Conteudo FROM AvisosPedido WHERE IdNotificacao = '{id}'");
        requisicao.Corpo.Should().Be(conteudo, "o corpo vai byte a byte como gravado");
        requisicao.Corpo.Should().Contain(pedidoDono.ToString());
        SchemaNotificacaoTestes.DeveSerValido(requisicao.Corpo).Should().BeTrue();
    }

    [Fact]
    public async Task Falha_e_depois_sucesso_tenta_de_novo_com_o_mesmo_webhook_id()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var receptor = await ReceptorWebhookTeste.IniciarAsync();
        receptor.Roteiro = n => (n == 1 ? 500 : 200, TimeSpan.Zero);
        var (destinoId, _) = await DestinoAsync(cliente, receptor.Url());
        await PedidoComAvisoAsync(cliente, operador);
        await using var worker = WorkerEnviando(mensageria, stringConexao);
        worker.CreateClient().Dispose();

        (await EsperarRecebidasAsync(receptor, 1)).Should().BeTrue();
        (await MensageriaTeste.EsperarAteAsync(async () =>
            await TentativasAsync(stringConexao, destinoId) == 1, Prazo)).Should().BeTrue();
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT UltimoCodigoHttp FROM EntregasNotificacao WHERE DestinoId = '{destinoId}'")).Should().Be(500);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT DATEDIFF(SECOND, PrimeiraTentativaEm, ProximaTentativaEm) FROM EntregasNotificacao WHERE DestinoId = '{destinoId}'")).Should().BeInRange(29, 31);
        await VencerAsync(stringConexao, destinoId);

        (await EsperarSituacaoAsync(stringConexao, destinoId, "Entregue")).Should().BeTrue();
        receptor.Recebidas.Should().HaveCount(2);
        receptor.Recebidas.Select(r => r.Cabecalhos[AssinaturaWebhook.CabecalhoId]).Distinct().Should().ContainSingle();
    }

    [Theory]
    [InlineData(302, "HTTP 302")]
    [InlineData(0, "tempo esgotado")]
    public async Task Redirecionamento_e_lentidao_contam_como_falha(int status, string erro)
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var receptor = await ReceptorWebhookTeste.IniciarAsync();
        receptor.Roteiro = _ => status == 0 ? (200, TimeSpan.FromSeconds(5)) : (status, TimeSpan.Zero);
        var (destinoId, _) = await DestinoAsync(cliente, receptor.Url());
        await PedidoComAvisoAsync(cliente, operador);
        await using var worker = WorkerEnviando(mensageria, stringConexao);
        worker.CreateClient().Dispose();

        (await MensageriaTeste.EsperarAteAsync(async () =>
            await TentativasAsync(stringConexao, destinoId) >= 1, Prazo)).Should().BeTrue();

        (await SituacaoEntregaAsync(stringConexao, destinoId)).Should().Be("Pendente");
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT ISNULL(UltimoErro, CONCAT('HTTP ', UltimoCodigoHttp)) FROM EntregasNotificacao WHERE DestinoId = '{destinoId}'"))
            .Should().Be(erro);
        receptor.Recebidas.Should().ContainSingle("o redirecionamento não é seguido");
    }

    [Fact]
    public async Task Gone_desativa_o_destino_na_hora_e_descarta_as_pendentes()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var receptor = await ReceptorWebhookTeste.IniciarAsync();
        receptor.Roteiro = _ => (410, TimeSpan.Zero);
        var (destinoId, _) = await DestinoAsync(cliente, receptor.Url());
        var pedido = await PedidoComAvisoAsync(cliente, operador);
        (await MudarStatusAsync(operador, pedido, "ProntoParaEnvio")).EnsureSuccessStatusCode();
        await using var worker = WorkerEnviando(mensageria, stringConexao);
        worker.CreateClient().Dispose();

        (await EsperarSituacaoAsync(stringConexao, destinoId, "Esgotada")).Should().BeTrue();
        (await MensageriaTeste.EsperarAteAsync(async () =>
            await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM EntregasNotificacao WHERE DestinoId = '{destinoId}' AND Situacao = N'Descartada'") == 1, Prazo))
            .Should().BeTrue("a segunda entrega do mesmo destino é descartada");
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT CONCAT(Ativo, '|', MotivoDesativacao) FROM DestinosNotificacao WHERE Id = '{destinoId}'"))
            .Should().Be("0|410 Gone");
        receptor.Recebidas.Should().ContainSingle();
    }

    [Fact]
    public async Task Vinte_e_quatro_horas_de_falha_esgotam_a_entrega_e_desativam_o_destino()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var receptor = await ReceptorWebhookTeste.IniciarAsync();
        receptor.Roteiro = _ => (503, TimeSpan.Zero);
        var (destinoId, _) = await DestinoAsync(cliente, receptor.Url());
        await PedidoComAvisoAsync(cliente, operador);
        await using var worker = WorkerEnviando(mensageria, stringConexao);
        worker.CreateClient().Dispose();
        (await EsperarRecebidasAsync(receptor, 1)).Should().BeTrue();
        (await MensageriaTeste.EsperarAteAsync(async () =>
            await TentativasAsync(stringConexao, destinoId) == 1, Prazo)).Should().BeTrue();

        await BancoDadosTeste.ExecutarAsync(stringConexao,
            $"UPDATE EntregasNotificacao SET PrimeiraTentativaEm = DATEADD(HOUR, -25, SYSDATETIMEOFFSET()) WHERE DestinoId = '{destinoId}'");
        await VencerAsync(stringConexao, destinoId);

        (await EsperarSituacaoAsync(stringConexao, destinoId, "Esgotada")).Should().BeTrue();
        (await BancoDadosTeste.ConsultarAsync<bool>(stringConexao, $"SELECT Ativo FROM DestinosNotificacao WHERE Id = '{destinoId}'")).Should().BeFalse();
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT MotivoDesativacao FROM DestinosNotificacao WHERE Id = '{destinoId}'")).Should().Contain("esgotada");
    }

    [Fact]
    public async Task Destino_fora_do_ar_nao_atrasa_o_outro()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var lento = await ReceptorWebhookTeste.IniciarAsync();
        lento.Roteiro = _ => (200, TimeSpan.FromSeconds(8));
        await using var rapido = await ReceptorWebhookTeste.IniciarAsync();
        await DestinoAsync(cliente, lento.Url());
        await DestinoAsync(cliente, rapido.Url());
        await PedidoComAvisoAsync(cliente, operador);
        // Tempo limite acima do atraso do lento: em série, o rápido só seria chamado 8 s depois.
        await using var worker = WorkerEnviando(mensageria, stringConexao,
            configuracoes: new Dictionary<string, string?> { ["Notificacoes:Envio:TempoLimiteSegundos"] = "15" });
        worker.CreateClient().Dispose();

        (await EsperarRecebidasAsync(lento, 1)).Should().BeTrue();
        (await EsperarRecebidasAsync(rapido, 1)).Should().BeTrue();
        // Relativo, e não absoluto: a partida do Worker sob carga não entra na conta.
        (rapido.Recebidas.First().RecebidaEm - lento.Recebidas.First().RecebidaEm).Duration()
            .Should().BeLessThan(TimeSpan.FromSeconds(4), "os dois destinos são chamados na mesma rodada, em paralelo");
    }

    [Fact]
    public async Task Destino_cadastrado_depois_do_aviso_nao_o_recebe()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var receptor = await ReceptorWebhookTeste.IniciarAsync();
        await PedidoComAvisoAsync(cliente, operador);
        var (destinoId, _) = await DestinoAsync(cliente, receptor.Url());
        var depois = await PedidoComAvisoAsync(cliente, operador);
        await using var worker = WorkerEnviando(mensageria, stringConexao);
        worker.CreateClient().Dispose();

        (await EsperarSituacaoAsync(stringConexao, destinoId, "Entregue")).Should().BeTrue();
        await Task.Delay(TimeSpan.FromSeconds(2), Token);
        receptor.Recebidas.Should().ContainSingle().Which.Corpo.Should().Contain(depois.ToString());
    }

    [Fact]
    public async Task Dois_Workers_nao_duplicam_entregas()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var receptor = await ReceptorWebhookTeste.IniciarAsync();
        var (destinoId, _) = await DestinoAsync(cliente, receptor.Url());
        for (var i = 0; i < 5; i++)
        {
            await PedidoComAvisoAsync(cliente, operador);
        }

        await using var a = WorkerEnviando(mensageria, stringConexao);
        await using var b = WorkerEnviando(mensageria, stringConexao);
        await Task.WhenAll(Task.Run(() => a.CreateClient().Dispose(), Token), Task.Run(() => b.CreateClient().Dispose(), Token));

        (await EsperarRecebidasAsync(receptor, 5)).Should().BeTrue();
        (await MensageriaTeste.EsperarAteAsync(async () =>
            await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM EntregasNotificacao WHERE DestinoId = '{destinoId}' AND Situacao = N'Entregue'") == 5, Prazo)).Should().BeTrue();
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM EntregasNotificacao WHERE DestinoId = '{destinoId}'")).Should().Be(5);
        receptor.Recebidas.Select(IdNotificacao).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Com_o_broker_fora_do_ar_o_webhook_sai_mesmo_assim()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var receptor = await ReceptorWebhookTeste.IniciarAsync();
        await DestinoAsync(cliente, receptor.Url());
        await PedidoComAvisoAsync(cliente, operador);
        var semBroker = new Dictionary<string, string?>(mensageria.ConfiguracoesBroker) { ["RabbitMQ:Porta"] = "1" };

        await using var worker = WorkerEnviando(mensageria, stringConexao, broker: semBroker);
        worker.CreateClient().Dispose();

        (await EsperarRecebidasAsync(receptor, 1)).Should().BeTrue();
    }

    [Fact]
    public async Task Segredo_gravado_com_outra_chave_falha_sem_enviar_nada()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var receptor = await ReceptorWebhookTeste.IniciarAsync();
        var (destinoId, _) = await DestinoAsync(cliente, receptor.Url());
        await BancoDadosTeste.ExecutarAsync(stringConexao, $"UPDATE DestinosNotificacao SET SegredoCifrado = CRYPT_GEN_RANDOM(60) WHERE Id = '{destinoId}'");
        await PedidoComAvisoAsync(cliente, operador);
        await using var worker = WorkerEnviando(mensageria, stringConexao);
        worker.CreateClient().Dispose();

        (await MensageriaTeste.EsperarAteAsync(async () =>
            await TentativasAsync(stringConexao, destinoId) >= 1, Prazo)).Should().BeTrue();
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT UltimoErro FROM EntregasNotificacao WHERE DestinoId = '{destinoId}'"))
            .Should().Be(EnviadorWebhook.ErroSegredoIlegivel);
        receptor.Recebidas.Should().BeEmpty();
    }
}
