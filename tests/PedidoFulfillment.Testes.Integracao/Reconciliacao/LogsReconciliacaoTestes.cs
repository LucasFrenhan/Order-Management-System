using FluentAssertions;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Reconciliacao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Entregas.CenarioEntrega;
using static PedidoFulfillment.Testes.Integracao.Pagamentos.CenarioPagamento;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;
using static PedidoFulfillment.Testes.Integracao.Reconciliacao.CenarioReconciliacao;

namespace PedidoFulfillment.Testes.Integracao.Reconciliacao;

/// <summary>
/// Logs da reconciliação para a operação (REQ-12, US3, FR-015): identificadores, etapa,
/// resultado e contagens, sem dados pessoais.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class LogsReconciliacaoTestes(MensageriaTeste mensageria)
{
    private static readonly TimeSpan UmaHora = TimeSpan.FromHours(1);

    [Fact]
    public async Task Cada_pedido_recuperado_e_o_resumo_do_ciclo_ficam_em_aviso()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, email, _) = await ClienteAsync(api);
        // Na ordem do fluxo de trás para a frente: cada Worker de preparação só vê o próprio pedido.
        var entrega = await ParadoNaEntregaAsync(mensageria, stringConexao, cliente);
        var pagamento = await ParadoNoPagamentoAsync(mensageria, stringConexao, cliente);
        var estoque = await ParadoNoEstoqueAsync(mensageria, stringConexao, cliente);
        foreach (var pedidoId in new[] { entrega, pagamento, estoque })
        {
            await EnvelhecerAsync(stringConexao, pedidoId, UmaHora);
        }

        using var logs = new CapturaLogs();

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao, logs);
            worker.CreateClient().Dispose();

            (await EsperarLogsAsync(logs, 9004)).Should().BeTrue();
            var recuperados = logs.Registros.Where(r => r.Evento.Id == 9001).ToList();
            recuperados.Should().HaveCount(3).And.OnlyContain(r => r.Nivel == LogLevel.Warning);
            foreach (var (pedidoId, etapa) in new[] { (entrega, EtapaFluxoPedido.Entrega), (pagamento, EtapaFluxoPedido.Pagamento), (estoque, EtapaFluxoPedido.Estoque) })
            {
                recuperados.Should().ContainSingle(r => r.Texto.Contains(pedidoId.ToString(), StringComparison.Ordinal)
                    && r.Texto.Contains($"Etapa={etapa}", StringComparison.Ordinal));
            }

            var resumo = logs.Registros.First(r => r.Evento.Id == 9004);
            resumo.Nivel.Should().Be(LogLevel.Warning);
            resumo.Texto.Should().Contain("Estoque=1").And.Contain("Pagamento=1").And.Contain("Entrega=1").And.Contain("Esgotados=0");

            // Só identificadores e contagens (FR-015).
            var valorTotal = (await ValorTotalAsync(stringConexao, entrega)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            logs.Registros.Where(r => r.Evento.Id is >= 9001 and <= 9007).Should()
                .NotContain(r => r.Texto.Contains(email, StringComparison.OrdinalIgnoreCase) || r.Texto.Contains(valorTotal, StringComparison.Ordinal));
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Ciclo_sem_pedidos_parados_nao_gera_aviso()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        // Parado há pouco: não é candidato.
        await ParadoNaEntregaAsync(mensageria, stringConexao, cliente);
        using var logs = new CapturaLogs();

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao, logs);
            worker.CreateClient().Dispose();

            (await EsperarLogsAsync(logs, 9004, quantidade: 2)).Should().BeTrue();
            logs.Registros.Where(r => r.Evento.Id == 9004).Should().OnlyContain(r => r.Nivel == LogLevel.Debug);
            logs.Registros.Should().NotContain(r => r.Evento.Id >= 9001 && r.Evento.Id <= 9007 && r.Nivel >= LogLevel.Warning);
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Esgotamento_fica_em_erro_com_a_etapa_e_as_falhas()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await ParadoNoEstoqueAsync(mensageria, stringConexao, cliente);
        await EnvelhecerAsync(stringConexao, pedidoId, TimeSpan.FromDays(1));
        using var logs = new CapturaLogs();

        try
        {
            await using var worker = WorkerReconciliando(mensageria, stringConexao, logs,
                new Dictionary<string, string?> { ["Reconciliacao:LimiteEsgotamentoSegundos"] = "120" }, RegraQueFalha(EtapaFluxoPedido.Estoque));
            worker.CreateClient().Dispose();

            (await EsperarLogsAsync(logs, 9002)).Should().BeTrue();
            var esgotado = logs.Registros.Single(r => r.Evento.Id == 9002);
            esgotado.Nivel.Should().Be(LogLevel.Error);
            esgotado.Texto.Should().Contain(pedidoId.ToString()).And.Contain("Etapa=Estoque").And.Contain("Falhas=3");
            var falhas = logs.Registros.Where(r => r.Evento.Id == 9003).Select(r => r.Texto).ToList();
            falhas.Should().HaveCountGreaterThanOrEqualTo(3);
            falhas[0].Should().Contain("Falhas=1");
            falhas[2].Should().Contain("Falhas=3");
        }
        finally
        {
            await LimparFilasAsync(mensageria);
        }
    }
}
