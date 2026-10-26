using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Entregas;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Pagamentos;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Estornos.CenarioEstorno;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Estornos;

/// <summary>
/// Corridas do cancelamento com a cobrança e com outro cancelamento, com SQL Server e RabbitMQ
/// reais e sincronização determinística (REQ-13, Edge Cases e R-07).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class DisputaCancelamentoTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cancelamento_gravado_antes_da_cobranca_impede_a_cobranca()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        Guid pedidoId;
        await using (await WorkerProntoAsync(mensageria, stringConexao))
        {
            pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
            (await MensageriaTeste.EsperarAteAsync(() => ReservadoAsync(stringConexao, pedidoId), Prazo)).Should().BeTrue();
        }

        await CancelarComoOperacaoAsync(await OperadorAsync(api), pedidoId);
        var logs = new CapturaLogs();
        await using var worker = await CenarioPagamento.WorkerProntoAsync(mensageria, stringConexao, taxaAprovacao: 100, logs);
        await CenarioPagamento.RepublicarEstoqueReservadoAsync(mensageria, stringConexao, pedidoId);

        (await EsperarLogAsync(logs, 7003, Prazo)).Should().BeTrue("o pedido cancelado é ignorado no pagamento");
        (await CenarioPagamento.PagamentosAsync(stringConexao, pedidoId)).Should().Be(0);
    }

    [Fact]
    public async Task Cobranca_aprovada_para_pedido_cancelado_no_meio_do_caminho_fica_com_estorno_pendente()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var gateway = new GatewayQueEspera();
        var logs = new CapturaLogs();
        // REQ-15: o processador de estornos ligado, para ver o pagamento.estornado do fim da história.
        await using var worker = await WorkerDePagamentoAsync(stringConexao, gateway, logs,
            new Dictionary<string, string?> { ["Estornos:Habilitado"] = "true", ["Estornos:IntervaloSegundos"] = "1" });

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        await gateway.Chamado.Task.WaitAsync(Prazo, Cancelamento);
        await CancelarComoOperacaoAsync(await OperadorAsync(api), pedidoId);
        gateway.Liberar();

        (await EsperarLogAsync(logs, 7009, Prazo)).Should().BeTrue("R-07: o Cliente foi cobrado, e o estorno fica pendente");
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("Cancelado");
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM EventosSaida WHERE Tipo = '{TiposEvento.PagamentoAprovado}' AND Conteudo LIKE '%{pedidoId}%'"))
            .Should().Be(0, "o pedido está cancelado: pagamento.aprovado não sai (REQ-15, FR-014)");
        gateway.Cobrancas.Should().Be(1);

        // REQ-15, US2 cenário 4: o cancelamento saiu sem estorno pendente; o estorno aparece só no pagamento.estornado.
        (await EventosOutbox.CanceladoAsync(stringConexao, pedidoId)).EstornoPendente.Should().BeFalse();
        (await EsperarSituacaoAsync(stringConexao, pedidoId, "Estornado")).Should().BeTrue();
        var estornado = (await EventosOutbox.ListarAsync<PagamentoEstornado>(stringConexao, pedidoId)).Should().ContainSingle().Subject.Dados;
        var valorCobrado = await BancoDadosTeste.ConsultarAsync<decimal>(stringConexao, $"SELECT Valor FROM Pagamentos WHERE PedidoId = '{pedidoId}'");
        (estornado.Valor, estornado.IdentificadorTransacao, estornado.IdentificadorEstorno)
            .Should().Be((valorCobrado, $"SIM-{pedidoId:N}", $"SIM-EST-{pedidoId:N}"));
    }

    [Fact]
    public async Task Dois_cancelamentos_simultaneos_cancelam_uma_vez_com_um_unico_estorno_pendente()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await PedidoPagoAsync(mensageria, stringConexao, cliente);
        var operador = await OperadorAsync(api);
        var admin = await AdminAsync(api);

        var respostas = await Task.WhenAll(
            MudarStatusAsync(operador, pedidoId, "Cancelado", "Primeiro."),
            MudarStatusAsync(admin, pedidoId, "Cancelado", "Segundo."));

        respostas.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("EstornoPendente");
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM HistoricosStatusPedido WHERE PedidoId = '{pedidoId}' AND StatusNovo = 'Cancelado'")).Should().Be(1);
        // REQ-15, US1 cenário 8: um único pedido.cancelado, do vencedor.
        (await EventosOutbox.ListarAsync<PedidoCancelado>(stringConexao, pedidoId)).Should().ContainSingle()
            .Which.Dados.EstornoPendente.Should().BeTrue();
    }

    [Fact]
    public async Task Cancelamento_e_despacho_ao_mesmo_tempo_nunca_deixam_rastreio_em_pedido_cancelado()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await PedidoProntoParaEnvioAsync(mensageria, stringConexao, cliente);
        var operador = await OperadorAsync(api);
        var admin = await AdminAsync(api);

        var respostas = await Task.WhenAll(
            MudarStatusAsync(operador, pedidoId, "Cancelado", "Cancelado na corrida."),
            RegistrarRastreioAsync(admin, pedidoId, "Correios", "BR123456789BR"));

        respostas.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        var status = await StatusAsync(stringConexao, pedidoId);
        var canceladas = await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM OrdensEntrega WHERE PedidoId = '{pedidoId}' AND CanceladaEm IS NOT NULL");
        var despachadas = await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM OrdensEntrega WHERE PedidoId = '{pedidoId}' AND DespachadoEm IS NOT NULL");
        var eventos = await EventosOutbox.ListarAsync<PedidoCancelado>(stringConexao, pedidoId);
        if (status == "Cancelado")
        {
            (canceladas, despachadas).Should().Be((1, 0));
            (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("EstornoPendente");
            eventos.Should().ContainSingle().Which.Dados.OrdemEntregaCancelada.Should().BeTrue();
        }
        else
        {
            status.Should().Be("Despachado");
            (canceladas, despachadas).Should().Be((0, 1));
            (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("Aprovado");
            eventos.Should().BeEmpty("o despacho venceu: nada foi cancelado");
        }
    }

    [Fact]
    public async Task Pedido_cancelado_antes_da_ordem_nao_ganha_ordem_quando_o_pagamento_aprovado_chega()
    {
        // US2 cenário 5: o consumidor de entrega ignora o pedido cancelado, como hoje.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await PedidoPagoAsync(mensageria, stringConexao, cliente);
        await CancelarComoOperacaoAsync(await OperadorAsync(api), pedidoId);
        var logs = new CapturaLogs();
        await using var worker = await Entregas.CenarioEntrega.WorkerProntoAsync(mensageria, stringConexao, logs);

        await Entregas.CenarioEntrega.PublicarPagamentoAprovadoAsync(mensageria, pedidoId);

        (await EsperarLogAsync(logs, 8002, Prazo)).Should().BeTrue("o pedido cancelado é ignorado na entrega");
        (await Entregas.CenarioEntrega.OrdensAsync(stringConexao, pedidoId)).Should().Be(0);
    }

    /// <summary>Worker de estoque e pagamento com o gateway dado, sem a entrega.</summary>
    private async Task<FabricaWorker> WorkerDePagamentoAsync(
        string stringConexao, IGatewayPagamento gateway, CapturaLogs logs, IReadOnlyDictionary<string, string?>? configuracoes = null)
    {
        await mensageria.LimparFilasConsumidorAsync(FilaEstoque);
        await mensageria.LimparFilasConsumidorAsync(CenarioPagamento.FilaPagamento);
        var worker = new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
            Configuracoes = configuracoes ?? new Dictionary<string, string?>(),
            Servicos = servicos =>
            {
                RemoverConsumidor<CriacaoOrdemEntrega, PagamentoAprovado>(servicos);
                servicos.AddSingleton(gateway);
                servicos.AddSingleton<ILoggerProvider>(logs);
            }
        };
        worker.CreateClient().Dispose();
        (await mensageria.ConsumidorLigadoAsync(FilaEstoque, TimeSpan.FromSeconds(10))).Should().BeTrue();
        (await mensageria.ConsumidorLigadoAsync(CenarioPagamento.FilaPagamento, TimeSpan.FromSeconds(10))).Should().BeTrue();
        return worker;
    }

    /// <summary>Aprova a cobrança só depois de <see cref="Liberar"/>, para o teste cancelar no meio.</summary>
    private sealed class GatewayQueEspera : IGatewayPagamento
    {
        private readonly TaskCompletionSource liberado = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int cobrancas;

        public TaskCompletionSource Chamado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Cobrancas => cobrancas;

        public void Liberar() => liberado.TrySetResult();

        public async Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref cobrancas);
            Chamado.TrySetResult();
            await liberado.Task.WaitAsync(cancellationToken);
            return ResultadoCobranca.Aprovada($"SIM-{pedidoId:N}");
        }

        public Task<ResultadoEstorno> EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken cancellationToken) =>
            Task.FromResult(ResultadoEstorno.Confirmar($"SIM-EST-{pedidoId:N}"));
    }
}
