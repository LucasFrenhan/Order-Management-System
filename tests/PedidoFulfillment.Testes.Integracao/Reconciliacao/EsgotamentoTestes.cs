using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Application.Reconciliacao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Entregas.CenarioEntrega;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pagamentos.CenarioPagamento;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;
using static PedidoFulfillment.Testes.Integracao.Reconciliacao.CenarioReconciliacao;

namespace PedidoFulfillment.Testes.Integracao.Reconciliacao;

/// <summary>
/// Esgotamento da reconciliação, com SQL Server e RabbitMQ reais (REQ-12, US2 cenários 8 a 12;
/// FR-011 a FR-013): ciclos de 1 s, esgotamento com 3 falhas e 120 s parado.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class EsgotamentoTestes(MensageriaTeste mensageria)
{
    private static readonly TimeSpan UmDia = TimeSpan.FromDays(1);

    private static readonly Dictionary<string, string?> Esgotamento = new()
    {
        ["Reconciliacao:CiclosFalhaEsgotamento"] = "3",
        ["Reconciliacao:LimiteEsgotamentoSegundos"] = "120"
    };

    [Fact]
    public async Task Cobranca_que_falha_sempre_se_esgota_com_pagamento_nao_processado_e_estoque_devolvido()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await ParadoNoPagamentoAsync(mensageria, stringConexao, cliente);
        await EnvelhecerAsync(stringConexao, pedidoId, UmDia);
        var eventosAntes = await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM EventosSaida");
        using var logs = new CapturaLogs();

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao, logs, Esgotamento,
                servicos => servicos.AddSingleton<IGatewayPagamento, GatewayQueFalha>());
            worker.CreateClient().Dispose();

            (await EsperarStatusAsync(stringConexao, pedidoId, "Cancelado", Prazo)).Should().BeTrue();
            (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("NaoProcessado", "a check constraint aceita a situação nova");
            (await UltimoMotivoSemAutorAsync(stringConexao, pedidoId)).Should().Be("Não foi possível processar o pagamento.");
            (await ReservadoAsync(stringConexao, pedidoId)).Should().BeFalse();
            (await EstoqueAsync(stringConexao, Cat001)).Should().Be(50);
            (await EstoqueAsync(stringConexao, Cat002)).Should().Be(100);
            (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM EventosSaida")).Should().Be(eventosAntes, "não existe evento de cancelamento");
            logs.Registros.Count(r => r.Evento.Id == 9003).Should().BeGreaterThanOrEqualTo(3);
            logs.Registros.Should().ContainSingle(r => r.Evento.Id == 9002).Which.Texto.Should().Contain("Falhas=3");
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Reserva_que_falha_sempre_se_esgota_e_cancela_sem_evento()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await ParadoNoEstoqueAsync(mensageria, stringConexao, cliente);
        await EnvelhecerAsync(stringConexao, pedidoId, UmDia);

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao, configuracoes: Esgotamento,
                servicos: RegraQueFalha(EtapaFluxoPedido.Estoque));
            worker.CreateClient().Dispose();

            (await EsperarStatusAsync(stringConexao, pedidoId, "Cancelado", Prazo)).Should().BeTrue();
            (await UltimoMotivoSemAutorAsync(stringConexao, pedidoId)).Should().Be("Não foi possível reservar o estoque.");
            (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM EventosSaida WHERE Tipo <> 'pedido.criado'")).Should().Be(0);
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Pedido_pago_cuja_ordem_falha_sempre_nunca_e_cancelado()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await ParadoNaEntregaAsync(mensageria, stringConexao, cliente);
        await EnvelhecerAsync(stringConexao, pedidoId, UmDia);
        using var logs = new CapturaLogs();

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao, logs, Esgotamento, RegraQueFalha(EtapaFluxoPedido.Entrega));
            worker.CreateClient().Dispose();

            (await EsperarLogsAsync(logs, 9003, quantidade: 5)).Should().BeTrue();
            (await StatusAsync(stringConexao, pedidoId)).Should().Be("Processando", "FR-013");
            (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("Aprovado");
            logs.Registros.Should().NotContain(r => r.Evento.Id == 9002);
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Falha_de_um_pedido_nao_impede_os_demais_no_mesmo_ciclo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var entrega = await ParadoNaEntregaAsync(mensageria, stringConexao, cliente);
        var estoque = await ParadoNoEstoqueAsync(mensageria, stringConexao, cliente);
        // O que falha vem antes no lote (mais antigo).
        await EnvelhecerAsync(stringConexao, estoque, TimeSpan.FromHours(2));
        await EnvelhecerAsync(stringConexao, entrega, TimeSpan.FromHours(1));
        using var logs = new CapturaLogs();

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao, logs, servicos: RegraQueFalha(EtapaFluxoPedido.Estoque));
            worker.CreateClient().Dispose();

            (await EsperarLogsAsync(logs, 9004)).Should().BeTrue();
            logs.Registros.First(r => r.Evento.Id == 9004).Texto.Should().Contain("Entrega=1").And.Contain("Falhas=1");
            (await OrdensAsync(stringConexao, entrega)).Should().Be(1);
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Lote_cheio_de_pedidos_que_falham_nao_impede_um_pedido_parado_novo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var entrega = await ParadoNaEntregaAsync(mensageria, stringConexao, cliente);
        var primeiro = await ParadoNoEstoqueAsync(mensageria, stringConexao, cliente);
        var segundo = await ParadoNoEstoqueAsync(mensageria, stringConexao, cliente);
        await EnvelhecerAsync(stringConexao, primeiro, TimeSpan.FromHours(3));
        await EnvelhecerAsync(stringConexao, segundo, TimeSpan.FromHours(2));
        await EnvelhecerAsync(stringConexao, entrega, TimeSpan.FromHours(1));
        using var logs = new CapturaLogs();

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao, logs,
                new Dictionary<string, string?> { ["Reconciliacao:TamanhoLote"] = "2" }, RegraQueFalha(EtapaFluxoPedido.Estoque));
            worker.CreateClient().Dispose();

            // Primeiro ciclo: os dois mais antigos, que falham. Segundo: o novo vem antes deles (SC-007).
            (await EsperarLogsAsync(logs, 9004, quantidade: 2)).Should().BeTrue();
            var resumos = logs.Registros.Where(r => r.Evento.Id == 9004).Select(r => r.Texto).ToList();
            resumos[0].Should().Contain("Entrega=0").And.Contain("Falhas=2");
            resumos[1].Should().Contain("Entrega=1");
            (await OrdensAsync(stringConexao, entrega)).Should().Be(1);
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    private sealed class GatewayQueFalha : IGatewayPagamento
    {
        public Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken) =>
            throw new TimeoutException("Gateway de pagamento sem resposta.");
    }
}
