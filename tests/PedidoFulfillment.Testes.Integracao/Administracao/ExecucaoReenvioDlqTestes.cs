using System.Text;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Administracao.CenarioReenvio;

namespace PedidoFulfillment.Testes.Integracao.Administracao;

/// <summary>
/// Execução dos pedidos de reenvio da DLQ pelo Worker, com SQL Server e RabbitMQ reais
/// (REQ-14, US2 e research R-04).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class ExecucaoReenvioDlqTestes(MensageriaTeste mensageria)
{
    [Fact]
    public async Task As_mensagens_nao_selecionadas_voltam_a_posicao_original_na_DLQ()
    {
        // R-04: o pedido examina só as 3 primeiras de 6; a devolvida (m2) precisa continuar antes
        // das que o pedido nem leu (m4 a m6). Se fosse para o fim da fila, a ordem quebraria.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        await PrepararFilasAsync(mensageria);
        var m1 = await NaDlqAsync(mensageria, TiposEvento.PedidoCriado);
        var m2 = await NaDlqAsync(mensageria, TiposEvento.EstoqueReservado);
        var m3 = await NaDlqAsync(mensageria, TiposEvento.PedidoCriado);
        var m4 = await NaDlqAsync(mensageria, TiposEvento.EstoqueReservado);
        var m5 = await NaDlqAsync(mensageria, TiposEvento.PedidoCriado);
        var m6 = await NaDlqAsync(mensageria, TiposEvento.EstoqueReservado);
        var pedido = await PedidoAsync(stringConexao, new FiltroReenvio(TiposEvento.PedidoCriado, null, null, null));

        await using (var worker = WorkerReenviando(mensageria, stringConexao, configuracoes: new Dictionary<string, string?> { ["ReenvioDlq:MensagensPorPedido"] = "3" }))
        {
            worker.CreateClient().Dispose();
            (await EsperarSituacaoAsync(stringConexao, pedido.Id, SituacaoReenvioDlq.Concluido)).Should().BeTrue();
        }

        (await LerAsync(stringConexao, pedido.Id)).DeveTerSido(reenviadas: 2, bloqueadas: 0, foraDoFiltro: 1);
        (await EsvaziarAsync(mensageria, Dlq)).Select(m => m.MessageId).Should().Equal([m2, m4, m5, m6], "a devolvida volta à posição original");
        var principal = await EsvaziarAsync(mensageria, Principal);
        principal.Select(m => m.MessageId).Should().Equal(m1, m3);
        foreach (var (_, cabecalhos) in principal)
        {
            cabecalhos.Should().NotContainKey("x-tentativas", "as tentativas recomeçam do zero");
            CabecalhosReenvio.ReenviosFeitos(cabecalhos).Should().Be(1);
            Encoding.UTF8.GetString((byte[])cabecalhos![CabecalhosReenvio.PedidoReenvio]!).Should().Be(pedido.Id.ToString());
        }
    }

    [Fact]
    public async Task Filtro_por_periodo_e_por_motivo_e_limite_de_reenvios()
    {
        // US2 cenários 4 e 5, clarify Q1.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        await PrepararFilasAsync(mensageria);
        var agora = DateTimeOffset.UtcNow;
        var antiga = await NaDlqAsync(mensageria, motivo: "TimeoutException: x", falhouEm: agora.AddDays(-3));
        var recenteTimeout = await NaDlqAsync(mensageria, motivo: "TimeoutException: y", falhouEm: agora.AddMinutes(-5));
        var recenteContrato = await NaDlqAsync(mensageria, motivo: "Contrato inválido", falhouEm: agora.AddMinutes(-5));
        var esgotada = await NaDlqAsync(mensageria, motivo: "TimeoutException: z", falhouEm: agora.AddMinutes(-5), reenvios: 3);
        var pedido = await PedidoAsync(stringConexao, new FiltroReenvio(null, agora.AddHours(-1), agora, "timeout"));
        var logs = new CapturaLogs();

        await using (var worker = WorkerReenviando(mensageria, stringConexao, logs))
        {
            worker.CreateClient().Dispose();
            (await EsperarSituacaoAsync(stringConexao, pedido.Id, SituacaoReenvioDlq.Concluido)).Should().BeTrue();
        }

        (await LerAsync(stringConexao, pedido.Id)).DeveTerSido(reenviadas: 1, bloqueadas: 1, foraDoFiltro: 2);
        (await EsvaziarAsync(mensageria, Principal)).Select(m => m.MessageId).Should().Equal(recenteTimeout);
        (await EsvaziarAsync(mensageria, Dlq)).Select(m => m.MessageId).Should().Equal([antiga, recenteContrato, esgotada], "a bloqueada fica na DLQ");
        var log = logs.Registros.Should().ContainSingle(r => r.Evento.Id == 5009).Subject;
        log.Texto.Should().Contain(pedido.AutorId.ToString()).And.Contain("1 reenviada(s)").And.Contain("1 bloqueada(s)");
    }

    [Fact]
    public async Task Evento_reenviado_e_processado_pelo_consumidor_e_o_duplicado_e_descartado()
    {
        // US2 cenários 2 e 7.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Pedidos.CenarioPedidos.Api(stringConexao);
        var (cliente, _, _) = await Pedidos.CenarioPedidos.ClienteAsync(api);
        var pedidoId = (await Pedidos.CenarioPedidos.CriadoAsync(cliente, (Pedidos.CenarioPedidos.Cat001, 1))).GetProperty("id").GetGuid();
        var corpo = await Estoque.CenarioEstoque.PedidoCriadoGravadoAsync(stringConexao, pedidoId);
        await BancoDadosTeste.ExecutarAsync(stringConexao, "UPDATE EventosSaida SET EnviadoEm = SYSDATETIMEOFFSET() WHERE EnviadoEm IS NULL");
        await PrepararFilasAsync(mensageria);
        var idEvento = SerializacaoEventos.Desserializar<PedidoCriado>(corpo).IdEvento.ToString();
        await NaDlqComIdAsync(mensageria, idEvento, corpo);
        var primeiro = await PedidoAsync(stringConexao);
        var logs = new CapturaLogs();

        await using var worker = WorkerReenviando(mensageria, stringConexao, logs, comEstoque: true);
        worker.CreateClient().Dispose();

        (await EsperarSituacaoAsync(stringConexao, primeiro.Id, SituacaoReenvioDlq.Concluido)).Should().BeTrue();
        (await MensageriaTeste.EsperarAteAsync(() => Estoque.CenarioEstoque.ReservadoAsync(stringConexao, pedidoId), Prazo)).Should().BeTrue("o evento reenviado foi processado");

        await NaDlqComIdAsync(mensageria, idEvento, corpo);
        var segundo = await PedidoAsync(stringConexao);
        (await EsperarSituacaoAsync(stringConexao, segundo.Id, SituacaoReenvioDlq.Concluido)).Should().BeTrue();
        (await Estoque.CenarioEstoque.EsperarLogAsync(logs, 5005, Prazo)).Should().BeTrue("o duplicado é descartado, sem efeito repetido");
        (await Estoque.CenarioEstoque.EstoqueAsync(stringConexao, Pedidos.CenarioPedidos.Cat001)).Should().Be(49);
    }

    [Fact]
    public async Task Mensagem_que_falha_de_novo_volta_a_DLQ_com_o_contador_de_reenvios()
    {
        // US2 cenário 8: corpo fora do contrato vai direto para a DLQ na primeira leitura.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        await PrepararFilasAsync(mensageria);
        var id = await NaDlqAsync(mensageria, corpo: "{\"invalido\":true}");
        var pedido = await PedidoAsync(stringConexao);

        await using var worker = WorkerReenviando(mensageria, stringConexao, comEstoque: true);
        worker.CreateClient().Dispose();

        (await EsperarSituacaoAsync(stringConexao, pedido.Id, SituacaoReenvioDlq.Concluido)).Should().BeTrue();
        (await MensageriaTeste.EsperarAteAsync(async () => await ContarAsync(mensageria, Dlq) == 1, Prazo)).Should().BeTrue();
        var voltou = (await EsvaziarAsync(mensageria, Dlq)).Should().ContainSingle().Subject;
        voltou.MessageId.Should().Be(id);
        CabecalhosReenvio.ReenviosFeitos(voltou.Cabecalhos).Should().Be(1);
    }

    [Fact]
    public async Task Pedidos_sao_executados_na_ordem_e_o_em_execucao_e_retomado_primeiro()
    {
        // FR-010: um de cada vez; uma execução que caiu é retomada.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        await PrepararFilasAsync(mensageria);
        await NaDlqAsync(mensageria);
        await NaDlqAsync(mensageria);
        var agora = DateTimeOffset.UtcNow;
        var pendenteAntigo = await PedidoAsync(stringConexao, registradoEm: agora.AddMinutes(-10));
        var pendenteNovo = await PedidoAsync(stringConexao, registradoEm: agora.AddMinutes(-5));
        var interrompido = await PedidoAsync(stringConexao, registradoEm: agora.AddMinutes(-1), emExecucao: true);

        await using (var worker = WorkerReenviando(mensageria, stringConexao))
        {
            worker.CreateClient().Dispose();
            (await EsperarSituacaoAsync(stringConexao, pendenteNovo.Id, SituacaoReenvioDlq.Concluido)).Should().BeTrue();
        }

        (await LerAsync(stringConexao, interrompido.Id)).DeveTerSido(reenviadas: 2, bloqueadas: 0, foraDoFiltro: 0);
        (await LerAsync(stringConexao, pendenteAntigo.Id)).DeveTerSido(reenviadas: 0, bloqueadas: 0, foraDoFiltro: 0);
        (await LerAsync(stringConexao, pendenteNovo.Id)).Situacao.Should().Be(SituacaoReenvioDlq.Concluido);
    }

    [Fact]
    public async Task Duas_instancias_nao_executam_o_mesmo_pedido_ao_mesmo_tempo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        await PrepararFilasAsync(mensageria);
        for (var i = 0; i < 5; i++)
        {
            await NaDlqAsync(mensageria);
        }

        var pedido = await PedidoAsync(stringConexao);
        var logs = new CapturaLogs();

        await using var primeiro = WorkerReenviando(mensageria, stringConexao, logs);
        await using var segundo = WorkerReenviando(mensageria, stringConexao, logs);
        await Task.WhenAll(Task.Run(() => primeiro.CreateClient().Dispose(), TestContext.Current.CancellationToken),
            Task.Run(() => segundo.CreateClient().Dispose(), TestContext.Current.CancellationToken));

        (await EsperarSituacaoAsync(stringConexao, pedido.Id, SituacaoReenvioDlq.Concluido)).Should().BeTrue();
        (await LerAsync(stringConexao, pedido.Id)).DeveTerSido(reenviadas: 5, bloqueadas: 0, foraDoFiltro: 0);
        (await ContarAsync(mensageria, Principal)).Should().Be(5, "cada mensagem foi reenviada uma vez");
        logs.Registros.Count(r => r.Evento.Id == 5009).Should().Be(1);
    }

    [Fact]
    public async Task Broker_fora_antes_da_execucao_deixa_o_pedido_pendente()
    {
        // US2 cenário 11 (analyze I2).
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        await PrepararFilasAsync(mensageria);
        await NaDlqAsync(mensageria);
        var pedido = await PedidoAsync(stringConexao);
        var logs = new CapturaLogs();
        var semBroker = new Dictionary<string, string?> { ["RabbitMQ:Porta"] = "1" };

        await using (var worker = WorkerReenviando(mensageria, stringConexao, logs, semBroker))
        {
            worker.CreateClient().Dispose();
            (await Estoque.CenarioEstoque.EsperarLogAsync(logs, 5010, Prazo)).Should().BeTrue();
            (await LerAsync(stringConexao, pedido.Id)).Situacao.Should().Be(SituacaoReenvioDlq.Pendente);
        }

        await using var comBroker = WorkerReenviando(mensageria, stringConexao);
        comBroker.CreateClient().Dispose();
        (await EsperarSituacaoAsync(stringConexao, pedido.Id, SituacaoReenvioDlq.Concluido)).Should().BeTrue();
    }

    [Fact]
    public async Task Falha_no_meio_marca_o_pedido_com_falha_e_nenhuma_mensagem_se_perde()
    {
        // FR-014: a fila de destino apagada faz a publicação falhar; nada sai da DLQ sem chegar.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        await PrepararFilasAsync(mensageria);
        for (var i = 0; i < 3; i++)
        {
            await NaDlqAsync(mensageria);
        }

        // O despachante declara a topologia na partida (REQ-12): apaga, espera ele recriar e apaga de
        // novo, para a fila faltar só quando o executor publicar.
        await ApagarPrincipalAsync();
        var logs = new CapturaLogs();
        PedidoReenvioDlq pedido;

        await using (var worker = WorkerReenviando(mensageria, stringConexao, logs))
        {
            worker.CreateClient().Dispose();
            (await MensageriaTeste.EsperarAteAsync(() => FilaExisteAsync(Principal), Prazo)).Should().BeTrue();
            await ApagarPrincipalAsync();
            pedido = await PedidoAsync(stringConexao);
            (await EsperarSituacaoAsync(stringConexao, pedido.Id, SituacaoReenvioDlq.Falhou)).Should().BeTrue();
        }

        var falhou = await LerAsync(stringConexao, pedido.Id);
        falhou.Reenviadas.Should().Be(0);
        falhou.MotivoFalha.Should().NotBeNullOrEmpty();
        logs.Registros.Should().Contain(r => r.Evento.Id == 5010);
        (await MensageriaTeste.EsperarAteAsync(async () => await ContarAsync(mensageria, Dlq) == 3, Prazo)).Should().BeTrue("as sem ack voltam à DLQ");
        await PrepararFilasAsync(mensageria);
    }

    private async Task ApagarPrincipalAsync()
    {
        await using var conexao = await mensageria.ConectarAsync();
        await using var canal = await conexao.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        await canal.QueueDeleteAsync(Principal, ifUnused: false, ifEmpty: false, cancellationToken: TestContext.Current.CancellationToken);
    }

    private async Task<bool> FilaExisteAsync(string fila)
    {
        try
        {
            await ContarAsync(mensageria, fila);
            return true;
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return false;
        }
    }

    private static async Task NaDlqComIdAsync(MensageriaTeste mensageria, string idEvento, string corpo)
    {
        await using var conexao = await mensageria.ConectarAsync();
        await using var canal = await conexao.CreateChannelAsync(
            new RabbitMQ.Client.CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            TestContext.Current.CancellationToken);
        var propriedades = new RabbitMQ.Client.BasicProperties
        {
            Persistent = true,
            MessageId = idEvento,
            Type = TiposEvento.PedidoCriado,
            Headers = new Dictionary<string, object?> { ["x-tentativas"] = 3, ["x-motivo"] = Encoding.UTF8.GetBytes("TimeoutException") }
        };
        await canal.BasicPublishAsync("", Dlq, false, propriedades, Encoding.UTF8.GetBytes(corpo), TestContext.Current.CancellationToken);
    }
}
