using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Application.Reconciliacao;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Pagamentos;
using PedidoFulfillment.Infrastructure.Reconciliacao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Mensageria;
using static PedidoFulfillment.Testes.Integracao.Entregas.CenarioEntrega;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pagamentos.CenarioPagamento;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;
using static PedidoFulfillment.Testes.Integracao.Reconciliacao.CenarioReconciliacao;

namespace PedidoFulfillment.Testes.Integracao.Reconciliacao;

/// <summary>
/// Reconciliação nas três etapas, com SQL Server e RabbitMQ reais (REQ-12, US2; contracts/
/// publicacao-e-reconciliacao.md): o pedido parado recebe o mesmo efeito e o mesmo evento de
/// saída que o consumidor da etapa gravaria.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class ReconciliacaoEtapasTestes(MensageriaTeste mensageria)
{
    private static readonly TimeSpan UmaHora = TimeSpan.FromHours(1);

    [Fact]
    public async Task Pedido_pago_sem_ordem_ganha_a_ordem_e_entrega_criada()
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

            (await EsperarOrdemAsync(stringConexao, pedidoId, Prazo)).Should().BeTrue("SC-005");
            (await StatusAsync(stringConexao, pedidoId)).Should().Be("ProntoParaEnvio");
            (await UltimoMotivoSemAutorAsync(stringConexao, pedidoId)).Should().Be("Ordem de entrega criada.");
            (await EventosAsync(stringConexao, pedidoId, TiposEvento.EntregaCriada)).Should().Be(1);
            SchemaEventosTestes.DeveSerValido(TiposEvento.EntregaCriada, await ConteudoAsync(stringConexao, pedidoId, TiposEvento.EntregaCriada)).Should().BeTrue();
            (await EsperarLogsAsync(logs, 9001)).Should().BeTrue();
            logs.Registros.Should().Contain(r => r.Evento.Id == 9001 && r.Texto.Contains(pedidoId.ToString(), StringComparison.Ordinal)
                && r.Texto.Contains(nameof(EtapaFluxoPedido.Entrega), StringComparison.Ordinal));
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Pedido_sem_reserva_tem_o_estoque_reservado_e_estoque_reservado_gravado()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await ParadoNoEstoqueAsync(mensageria, stringConexao, cliente);
        await EnvelhecerAsync(stringConexao, pedidoId, UmaHora);

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao);
            worker.CreateClient().Dispose();

            (await MensageriaTeste.EsperarAteAsync(() => ReservadoAsync(stringConexao, pedidoId), Prazo)).Should().BeTrue();
            (await EstoqueAsync(stringConexao, Cat001)).Should().Be(48);
            (await StatusAsync(stringConexao, pedidoId)).Should().Be("Pendente");
            (await EventosAsync(stringConexao, pedidoId, TiposEvento.EstoqueReservado)).Should().Be(1);
            SchemaEventosTestes.DeveSerValido(TiposEvento.EstoqueReservado, await ConteudoAsync(stringConexao, pedidoId, TiposEvento.EstoqueReservado)).Should().BeTrue();
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Pedido_sem_reserva_e_sem_estoque_e_cancelado_com_estoque_insuficiente()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await ParadoNoEstoqueAsync(mensageria, stringConexao, cliente);
        await BancoDadosTeste.ExecutarAsync(stringConexao, "UPDATE Produtos SET QuantidadeEstoque = 0 WHERE Sku = 'CAT-002'");
        await EnvelhecerAsync(stringConexao, pedidoId, UmaHora);

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao);
            worker.CreateClient().Dispose();

            (await EsperarStatusAsync(stringConexao, pedidoId, "Cancelado", Prazo)).Should().BeTrue();
            (await UltimoMotivoSemAutorAsync(stringConexao, pedidoId)).Should().Be("Estoque insuficiente.");
            (await EventosAsync(stringConexao, pedidoId, TiposEvento.EstoqueInsuficiente)).Should().Be(1);
            (await EstoqueAsync(stringConexao, Cat001)).Should().Be(50);
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Theory]
    [InlineData(100, "Aprovado", "Processando", TiposEvento.PagamentoAprovado)]
    [InlineData(0, "Recusado", "PagamentoRejeitado", TiposEvento.PagamentoRecusado)]
    public async Task Pedido_reservado_sem_cobranca_e_cobrado_como_no_consumidor(int taxa, string situacao, string status, string evento)
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await ParadoNoPagamentoAsync(mensageria, stringConexao, cliente);
        await EnvelhecerAsync(stringConexao, pedidoId, UmaHora);

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao,
                configuracoes: new Dictionary<string, string?> { ["GatewayPagamento:TaxaAprovacaoPercentual"] = taxa.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            worker.CreateClient().Dispose();

            (await EsperarStatusAsync(stringConexao, pedidoId, status, Prazo)).Should().BeTrue();
            (await SituacaoAsync(stringConexao, pedidoId)).Should().Be(situacao);
            (await EventosAsync(stringConexao, pedidoId, evento)).Should().Be(1);
            SchemaEventosTestes.DeveSerValido(evento, await ConteudoAsync(stringConexao, pedidoId, evento)).Should().BeTrue();
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Cobranca_perdida_e_repetida_pela_reconciliacao_recebe_a_mesma_transacao()
    {
        // REQ-13, US4 cenário 4: o consumidor cobrou e não gravou; a reconciliação cobra com a mesma chave.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await ParadoNoPagamentoAsync(mensageria, stringConexao, cliente);
        await EnvelhecerAsync(stringConexao, pedidoId, UmaHora);
        var gateway = new GatewayPagamentoSimulado(Options.Create(new OpcoesGatewayPagamento { TaxaAprovacaoPercentual = 100 }));
        var perdida = await gateway.CobrarAsync(pedidoId, 0m, TestContext.Current.CancellationToken);

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao,
                servicos: servicos => servicos.AddSingleton<IGatewayPagamento>(gateway));
            worker.CreateClient().Dispose();

            (await EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
            (await TransacaoAsync(stringConexao, pedidoId)).Should().Be(perdida.IdentificadorTransacao, "sem segunda cobrança");
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Pedido_que_avancou_ha_pouco_nao_e_tocado()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await ParadoNaEntregaAsync(mensageria, stringConexao, cliente);
        using var logs = new CapturaLogs();

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao, logs);
            worker.CreateClient().Dispose();

            (await EsperarLogsAsync(logs, 9004, quantidade: 2)).Should().BeTrue("dois ciclos completos");
            (await OrdensAsync(stringConexao, pedidoId)).Should().Be(0, "SC-004");
            (await StatusAsync(stringConexao, pedidoId)).Should().Be("Processando");
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Theory]
    [InlineData("Cancelado")]
    [InlineData("Despachado")]
    [InlineData("PagamentoRejeitado")]
    public async Task Pedido_em_status_final_nao_muda(string status)
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await ParadoNoEstoqueAsync(mensageria, stringConexao, cliente);
        await BancoDadosTeste.ExecutarAsync(stringConexao, $"UPDATE Pedidos SET Status = '{status}' WHERE Id = '{pedidoId}'");
        await EnvelhecerAsync(stringConexao, pedidoId, UmaHora);
        using var logs = new CapturaLogs();

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao, logs);
            worker.CreateClient().Dispose();

            (await EsperarLogsAsync(logs, 9004, quantidade: 2)).Should().BeTrue();
            (await StatusAsync(stringConexao, pedidoId)).Should().Be(status);
            (await ReservadoAsync(stringConexao, pedidoId)).Should().BeFalse();
            (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM EventosSaida WHERE Tipo <> 'pedido.criado'")).Should().Be(0);
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Evento_original_que_chega_depois_da_recuperacao_e_ignorado()
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
            await using var worker = WorkerReconciliando(mensageria, stringConexao, logs, comConsumidores: true);
            worker.CreateClient().Dispose();
            (await EsperarOrdemAsync(stringConexao, pedidoId, Prazo)).Should().BeTrue();
            (await mensageria.ConsumidorLigadoAsync(FilaEntrega, Prazo)).Should().BeTrue();

            // O pagamento.aprovado original, que estava "perdido", chega agora (cenário 5 da US2).
            await RepublicarPagamentoAprovadoAsync(mensageria, stringConexao, pedidoId);

            (await EsperarLogsAsync(logs, 8002)).Should().BeTrue("o consumidor encontra o pedido não apto");
            (await OrdensAsync(stringConexao, pedidoId)).Should().Be(1);
            (await EventosAsync(stringConexao, pedidoId, TiposEvento.EntregaCriada)).Should().Be(1);
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Selecao_por_etapa_e_a_mesma_do_dominio()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (_, email, _) = await ClienteAsync(api);
        var clienteId = await BancoDadosTeste.ConsultarAsync<Guid>(stringConexao, $"SELECT Id FROM Usuarios WHERE Email = '{email}'");
        var antigo = DateTimeOffset.UtcNow.AddDays(-1);
        var pedidos = new List<Pedido>();

        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            var produtos = await contexto.Produtos.Where(p => p.Id == Cat001).ToDictionaryAsync(p => p.Id, TestContext.Current.CancellationToken);
            StatusPedido[] status = [.. Enum.GetValues<StatusPedido>()];
            string?[] pagamentos = [null, "Aprovado", "Recusado"];
            foreach (var alvo in status)
            {
                foreach (var reservado in new[] { false, true })
                {
                    foreach (var pagamento in pagamentos)
                    {
                        foreach (var comOrdem in pagamento == "Aprovado" ? new[] { false, true } : [false])
                        {
                            var pedido = new Pedido(clienteId, $"PED-20261020-{pedidos.Count + 1:D6}", [(produtos[Cat001], 1)], antigo);
                            if (pagamento is not null)
                            {
                                pedido.ReservarEstoque(produtos, antigo).Should().BeEmpty();
                                if (pagamento == "Aprovado")
                                {
                                    pedido.AprovarPagamento("SIM-1", antigo);
                                }
                                else
                                {
                                    pedido.RecusarPagamento("Recusado.", produtos, antigo);
                                }
                            }

                            if (comOrdem)
                            {
                                pedido.CriarOrdemEntrega(antigo);
                            }

                            if (pedido.Status != alvo)
                            {
                                pedido.MudarStatus(alvo, null, null, antigo);
                            }

                            typeof(Pedido).GetProperty(nameof(Pedido.EstoqueReservado))!.SetValue(pedido, reservado);
                            pedidos.Add(pedido);
                        }
                    }
                }
            }

            contexto.Pedidos.AddRange(pedidos);
            await contexto.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var leitura = BancoDadosTeste.NovoContexto(stringConexao);
        var selecionados = (await new ConsultaPedidosParados(leitura).ListarAsync(DateTimeOffset.UtcNow, 1000, [], TestContext.Current.CancellationToken))
            .ToDictionary(p => p.PedidoId, p => p.Etapa);

        pedidos.Should().HaveCount(48);
        foreach (var pedido in pedidos)
        {
            selecionados.GetValueOrDefault(pedido.Id).Should().Be(Esperada(pedido),
                $"status {pedido.Status}, reservado {pedido.EstoqueReservado}, pagamento {pedido.Pagamento?.Situacao}, ordem {pedido.OrdemEntrega is not null}");
        }
    }

    [Fact]
    public async Task Pedidos_que_ja_falharam_vem_depois_dos_demais()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var maisAntigo = await ParadoNoEstoqueAsync(mensageria, stringConexao, cliente);
        var meio = await ParadoNoEstoqueAsync(mensageria, stringConexao, cliente);
        var novo = await ParadoNoEstoqueAsync(mensageria, stringConexao, cliente);
        await EnvelhecerAsync(stringConexao, maisAntigo, TimeSpan.FromHours(3));
        await EnvelhecerAsync(stringConexao, meio, TimeSpan.FromHours(2));
        await EnvelhecerAsync(stringConexao, novo, UmaHora);

        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var consulta = new ConsultaPedidosParados(contexto);
        var corte = DateTimeOffset.UtcNow.AddMinutes(-1);

        (await consulta.ListarAsync(corte, 2, [maisAntigo, meio], TestContext.Current.CancellationToken)).Select(p => p.PedidoId)
            .Should().Equal(novo, maisAntigo);
        (await consulta.ListarAsync(corte, 3, [maisAntigo, meio], TestContext.Current.CancellationToken)).Select(p => p.PedidoId)
            .Should().Equal(novo, maisAntigo, meio);
        (await consulta.ListarAsync(corte, 2, [], TestContext.Current.CancellationToken)).Select(p => p.PedidoId)
            .Should().Equal(maisAntigo, meio);
    }

    [Fact]
    public void Worker_com_opcao_de_reconciliacao_invalida_nao_sobe()
    {
        using var worker = new FabricaWorker
        {
            StringConexao = FabricaApi.StringConexaoTeste,
            ConfiguracoesBroker = new Dictionary<string, string?>
            {
                ["RabbitMQ:Servidor"] = "127.0.0.1",
                ["RabbitMQ:Porta"] = "1",
                ["RabbitMQ:Usuario"] = "oms",
                ["RabbitMQ:Senha"] = "oms",
                ["RabbitMQ:VirtualHost"] = "/"
            },
            Configuracoes = new Dictionary<string, string?> { ["Reconciliacao:TamanhoLote"] = "0" }
        };

        var excecao = Record.Exception(() => worker.CreateClient());

        excecao.Should().NotBeNull();
        string.Join(" | ", worker.FalhaPartida.Cadeia(excecao!).Select(e => e.Message))
            .Should().Contain("Reconciliacao:TamanhoLote deve estar entre 1 e 1000.");
    }

    private static EtapaFluxoPedido Esperada(Pedido pedido) =>
        !pedido.EstoqueReservado && pedido.Status is StatusPedido.Pendente or StatusPedido.Processando or StatusPedido.ProntoParaEnvio
            ? EtapaFluxoPedido.Estoque
            : pedido.AptoParaPagamento
                ? EtapaFluxoPedido.Pagamento
                : pedido.AptoParaOrdemEntrega ? EtapaFluxoPedido.Entrega : default;
}
