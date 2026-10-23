using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Mensageria;
using static PedidoFulfillment.Testes.Integracao.Entregas.CenarioEntrega;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pagamentos.CenarioPagamento;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Entregas;

/// <summary>
/// Ordem de entrega criada pelo Worker completo, com SQL Server e RabbitMQ reais e a taxa do
/// gateway em 100% (REQ-10, US1; contracts/consumidor-entrega.md).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class OrdemEntregaCriadaTestes(MensageriaTeste mensageria)
{
    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Pedido_pago_ganha_a_ordem_fica_pronto_para_envio_e_e_anunciado_em_ate_10_segundos()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync(TiposEvento.EntregaCriada);
        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, stringConexao);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2), (Cat002, 1))).GetProperty("id").GetGuid();

        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(DoPedido(fila, pedidoId).Count > 0), Prazo)).Should().BeTrue("SC-005");
        var mensagem = DoPedido(fila, pedidoId).Should().ContainSingle().Subject;
        mensagem.Propriedades.Type.Should().Be(TiposEvento.EntregaCriada);
        SchemaEventosTestes.DeveSerValido(TiposEvento.EntregaCriada, mensagem.Texto).Should().BeTrue();
        var envelope = SerializacaoEventos.Desserializar<EntregaCriada>(mensagem.Texto);

        var ordem = (await OrdemAsync(stringConexao, pedidoId)).Should().NotBeNull().And.Subject.As<OrdemGravada>();
        ordem.SemDespacho.Should().BeTrue("transportadora, rastreio e despacho chegam com a issue #11");
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("ProntoParaEnvio");
        (await UltimoMotivoSemAutorAsync(stringConexao, pedidoId)).Should().Be("Ordem de entrega criada.");
        var numero = await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT Numero FROM Pedidos WHERE Id = '{pedidoId}'");
        envelope.IdCorrelacao.Should().Be(pedidoId);
        (envelope.Dados.PedidoId, envelope.Dados.Numero, envelope.Dados.OrdemEntregaId).Should().Be((pedidoId, numero, ordem.Id));
        envelope.Dados.Itens.Select(i => (i.ProdutoId, i.Quantidade)).Should().BeEquivalentTo([(Cat001, 2), (Cat002, 1)]);

        // FR-006: a reserva e o pagamento seguem como o pagamento deixou.
        (await ReservadoAsync(stringConexao, pedidoId)).Should().BeTrue();
        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(48);
        (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("Aprovado");
        (await PagamentosAsync(stringConexao, pedidoId)).Should().Be(1);
    }

    [Fact]
    public async Task Pagamento_aprovado_entregue_de_novo_nao_cria_outra_ordem()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync(TiposEvento.EntregaCriada);
        var logs = new CapturaLogs();
        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, stringConexao, logs);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await EsperarOrdemAsync(stringConexao, pedidoId, Prazo)).Should().BeTrue();
        var ordem = await OrdemAsync(stringConexao, pedidoId);

        await RepublicarPagamentoAprovadoAsync(mensageria, stringConexao, pedidoId);

        (await EsperarAteAsync(logs, 5005)).Should().BeTrue("o duplicado é descartado pela base do REQ-06");
        (await OrdensAsync(stringConexao, pedidoId)).Should().Be(1);
        (await OrdemAsync(stringConexao, pedidoId)).Should().Be(ordem);
        (await EntregasCriadasAsync(stringConexao, pedidoId)).Should().Be(1);
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("ProntoParaEnvio");
        DoPedido(fila, pedidoId).Should().ContainSingle();
    }

    private static Task<bool> EsperarAteAsync(CapturaLogs logs, int eventId) => EsperarLogAsync(logs, eventId, Prazo);
}
