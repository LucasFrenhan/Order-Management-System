using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pagamentos.CenarioPagamento;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pagamentos;

/// <summary>
/// Pedido que mudou antes do pagamento, com SQL Server e RabbitMQ reais (REQ-09, US4;
/// research R-05 e R-07).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class PedidoIgnoradoNoPagamentoTestes(MensageriaTeste mensageria)
{
    /// <summary>1 execução mais as 3 novas tentativas da base (REQ-06).</summary>
    private const int ExecucoesDaBase = 4;

    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(5);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pedido_cancelado_antes_do_pagamento_nao_e_cobrado_nem_devolve_o_estoque_de_novo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync("pagamento.*");
        using var logs = new CapturaLogs();

        // Reserva com o Worker só de estoque; cancelamento com ele parado (achado H1).
        var estoque = await Estoque.CenarioEstoque.WorkerProntoAsync(mensageria, stringConexao);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2))).GetProperty("id").GetGuid();
        (await MensageriaTeste.EsperarAteAsync(() => ReservadoAsync(stringConexao, pedidoId), Prazo)).Should().BeTrue();
        await estoque.DisposeAsync();
        (await MudarStatusAsync(cliente, pedidoId, "Cancelado", "Desisti")).StatusCode.Should().Be(HttpStatusCode.OK);
        var depoisDoCancelamento = await EstoqueAsync(stringConexao, Cat001);

        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, taxaAprovacao: 0, logs);
        await PublicarEstoqueReservadoAsync(mensageria, pedidoId);

        (await EsperarLogAsync(logs, 7003, Prazo)).Should().BeTrue("o cancelado é ignorado e confirmado");
        depoisDoCancelamento.Should().Be(50);
        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(depoisDoCancelamento, "SC-005");
        (await PagamentosAsync(stringConexao, pedidoId)).Should().Be(0);
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("Cancelado");
        DoPedido(fila, pedidoId).Should().BeEmpty();
        (await EsperarNaDlqDoPagamentoAsync(pedidoId, TimeSpan.FromMilliseconds(500))).Should().BeFalse();
    }

    [Fact]
    public async Task Estoque_reservado_de_pedido_inexistente_vai_para_a_dlq_depois_das_tentativas()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        await ClienteAsync(api);
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, taxaAprovacao: 100);
        var pedidoId = Guid.CreateVersion7();

        await PublicarEstoqueReservadoAsync(mensageria, pedidoId);

        (await EsperarNaDlqDoPagamentoAsync(pedidoId, TimeSpan.FromSeconds(10))).Should().BeTrue("4 execuções com 100, 200 e 400 ms entre elas");
    }

    [Fact]
    public async Task Cancelamento_e_recusa_simultaneos_devolvem_o_estoque_uma_vez_so()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);

        for (var rodada = 1; rodada <= 10; rodada++)
        {
            var produtoId = await NovoProdutoAsync(stringConexao, 1);
            var pedidoId = (await CriadoAsync(cliente, (produtoId, 1))).GetProperty("id").GetGuid();
            await Estoque.DisputaEstoqueTestes.ReservarComoABaseAsync(api.Services, pedidoId);
            (await EstoqueAsync(stringConexao, produtoId)).Should().Be(0, $"rodada {rodada}");

            var cancelamento = MudarStatusAsync(operador, pedidoId, "Cancelado", "Cliente desistiu");
            var pagamento = PagarComoABaseAsync(api.Services, pedidoId, ResultadoCobranca.Recusada(MotivoRecusa));
            await Task.WhenAll(cancelamento, pagamento);

            var status = await StatusAsync(stringConexao, pedidoId);
            (await EstoqueAsync(stringConexao, produtoId)).Should().Be(1, $"rodada {rodada}: devolvido uma vez só, pelo lado que venceu");
            (await ReservadoAsync(stringConexao, pedidoId)).Should().BeFalse($"rodada {rodada}");
            if (status == "Cancelado")
            {
                (await cancelamento).StatusCode.Should().Be(HttpStatusCode.OK, $"rodada {rodada}");
                (await PagamentosAsync(stringConexao, pedidoId)).Should().Be(0, $"rodada {rodada}");
            }
            else
            {
                status.Should().Be("PagamentoRejeitado", $"rodada {rodada}");
                (await cancelamento).StatusCode.Should().Be(HttpStatusCode.Conflict, $"rodada {rodada}: status final");
                (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("Recusado", $"rodada {rodada}");
            }
        }
    }

    /// <summary>
    /// Executa <see cref="ProcessamentoPagamento"/> como a base, com o resultado do gateway fixo:
    /// escopo novo, transação, efeito e commit; em exceção, desfaz e tenta de novo.
    /// </summary>
    private static async Task PagarComoABaseAsync(IServiceProvider servicos, Guid pedidoId, ResultadoCobranca resultado)
    {
        var evento = EnvelopeEvento.Criar(pedidoId, new EstoqueReservado
        {
            PedidoId = pedidoId,
            Itens = [new ItemEstoqueReservado { ProdutoId = Guid.CreateVersion7(), Quantidade = 1 }]
        }, DateTimeOffset.UtcNow);
        for (var execucao = 1; ; execucao++)
        {
            await using var escopo = servicos.CreateAsyncScope();
            var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
            await using var transacao = await contexto.Database.BeginTransactionAsync(Cancelamento);
            var consumidor = new ProcessamentoPagamento(
                escopo.ServiceProvider.GetRequiredService<IArmazenamentoPedidos>(), new GatewayFixo(resultado),
                TimeProvider.System, NullLogger<ProcessamentoPagamento>.Instance);
            try
            {
                await consumidor.ManipularAsync(evento, Cancelamento);
                await contexto.SaveChangesAsync(Cancelamento);
                await transacao.CommitAsync(Cancelamento);
                return;
            }
            catch (Exception) when (execucao < ExecucoesDaBase)
            {
                await transacao.RollbackAsync(Cancelamento);
            }
        }
    }

    private static async Task<Guid> NovoProdutoAsync(string stringConexao, int estoque)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var produto = new Produto($"PAG-{Guid.NewGuid():N}"[..20], "Produto disputado", null, 10m, estoque, true, DateTimeOffset.UtcNow);
        contexto.Produtos.Add(produto);
        await contexto.SaveChangesAsync(Cancelamento);
        return produto.Id;
    }

    /// <summary>Se chega a <c>oms.pagamento.dlq</c>, até o prazo, uma mensagem que cite o pedido.</summary>
    private async Task<bool> EsperarNaDlqDoPagamentoAsync(Guid pedidoId, TimeSpan prazo)
    {
        await using var conexao = await mensageria.ConectarAsync();
        await using var canal = await conexao.CreateChannelAsync(cancellationToken: Cancelamento);
        var encontrada = false;
        await MensageriaTeste.EsperarAteAsync(async () =>
        {
            while (await canal.BasicGetAsync(Infrastructure.Mensageria.TopologiaMensageria.FilaMortas(FilaPagamento), autoAck: true, Cancelamento) is { } mensagem)
            {
                encontrada |= System.Text.Encoding.UTF8.GetString(mensagem.Body.Span).Contains(pedidoId.ToString(), StringComparison.Ordinal);
            }

            return encontrada;
        }, prazo);
        return encontrada;
    }

    private sealed class GatewayFixo(ResultadoCobranca resultado) : IGatewayPagamento
    {
        public Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken) => Task.FromResult(resultado);
    }
}
