using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Mensageria;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pagamentos.CenarioPagamento;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pagamentos;

/// <summary>
/// Pagamento aprovado pelo Worker, com SQL Server e RabbitMQ reais e a taxa fixada em 100%
/// (REQ-09, US1; contracts/consumidor-pagamento.md).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class PagamentoAprovadoTestes(MensageriaTeste mensageria)
{
    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Pedido_reservado_e_pago_vai_para_processando_e_e_anunciado_em_ate_5_segundos()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync("pagamento.*");
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, taxaAprovacao: 100);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2), (Cat002, 1))).GetProperty("id").GetGuid();

        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(DoPedido(fila, pedidoId).Count > 0), Prazo)).Should().BeTrue("SC-001");
        var mensagem = DoPedido(fila, pedidoId).Should().ContainSingle().Subject;
        mensagem.Propriedades.Type.Should().Be(TiposEvento.PagamentoAprovado);
        SchemaEventosTestes.DeveSerValido(TiposEvento.PagamentoAprovado, mensagem.Texto).Should().BeTrue();
        var envelope = SerializacaoEventos.Desserializar<PagamentoAprovado>(mensagem.Texto);

        var valorTotal = await ValorTotalAsync(stringConexao, pedidoId);
        var transacao = await TransacaoAsync(stringConexao, pedidoId);
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("Processando");
        (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("Aprovado");
        (await ValorPagoAsync(stringConexao, pedidoId)).Should().Be(valorTotal);
        transacao.Should().StartWith("SIM-").And.HaveLength(36);
        (await UltimoMotivoSemAutorAsync(stringConexao, pedidoId)).Should().Be("Pagamento aprovado.");
        (await ReservadoAsync(stringConexao, pedidoId)).Should().BeTrue("a mercadoria segue para o envio");
        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(48);
        envelope.IdCorrelacao.Should().Be(pedidoId);
        (envelope.Dados.PedidoId, envelope.Dados.Valor, envelope.Dados.IdentificadorTransacao).Should().Be((pedidoId, valorTotal, transacao));
    }

    [Fact]
    public async Task Estoque_reservado_entregue_de_novo_nao_cobra_de_novo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync("pagamento.*");
        var logs = new CapturaLogs();
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, taxaAprovacao: 100, logs);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
        // O status é gravado junto com o outbox, mas o evento sai depois, pelo despachante: espera a
        // mensagem original antes da reentrega, senão o 5005 pode chegar antes dela.
        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(DoPedido(fila, pedidoId).Count > 0), Prazo)).Should().BeTrue();
        var transacao = await TransacaoAsync(stringConexao, pedidoId);

        await RepublicarEstoqueReservadoAsync(mensageria, stringConexao, pedidoId);

        (await EsperarLogAsync(logs, 5005, Prazo)).Should().BeTrue("o duplicado é descartado pela base do REQ-06");
        (await PagamentosAsync(stringConexao, pedidoId)).Should().Be(1);
        (await TransacaoAsync(stringConexao, pedidoId)).Should().Be(transacao);
        DoPedido(fila, pedidoId).Should().ContainSingle();
    }
}
