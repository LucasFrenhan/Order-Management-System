using System.Net;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Mensageria;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pagamentos.CenarioPagamento;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pagamentos;

/// <summary>
/// Pagamento recusado pelo Worker, com a taxa fixada em 0%: pedido rejeitado e estoque
/// devolvido na mesma gravação (REQ-09, US2; contracts/consumidor-pagamento.md).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class PagamentoRecusadoTestes(MensageriaTeste mensageria)
{
    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Pagamento_recusado_rejeita_o_pedido_devolve_o_estoque_e_e_anunciado()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var admin = await AdminAsync(api);
        await using var fila = await mensageria.FilaTesteAsync("pagamento.*");
        using var logs = new CapturaLogs();
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, taxaAprovacao: 0, logs);
        var antes = (await EstoqueAsync(stringConexao, Cat001), await EstoqueAsync(stringConexao, Cat002));

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2), (Cat002, 1))).GetProperty("id").GetGuid();

        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(DoPedido(fila, pedidoId).Count > 0), Prazo)).Should().BeTrue();
        var mensagem = DoPedido(fila, pedidoId).Should().ContainSingle().Subject;
        mensagem.Propriedades.Type.Should().Be(TiposEvento.PagamentoRecusado);
        SchemaEventosTestes.DeveSerValido(TiposEvento.PagamentoRecusado, mensagem.Texto).Should().BeTrue();
        var dados = SerializacaoEventos.Desserializar<PagamentoRecusado>(mensagem.Texto).Dados;
        var valorTotal = await ValorTotalAsync(stringConexao, pedidoId);
        (dados.PedidoId, dados.Valor, dados.Motivo).Should().Be((pedidoId, valorTotal, MotivoRecusa));

        (await StatusAsync(stringConexao, pedidoId)).Should().Be("PagamentoRejeitado");
        (await SituacaoAsync(stringConexao, pedidoId)).Should().Be("Recusado");
        (await ValorPagoAsync(stringConexao, pedidoId)).Should().Be(valorTotal);
        (await ReservadoAsync(stringConexao, pedidoId)).Should().BeFalse();
        (await UltimoMotivoSemAutorAsync(stringConexao, pedidoId)).Should().Be(MotivoRecusa);
        (await EstoqueAsync(stringConexao, Cat001), await EstoqueAsync(stringConexao, Cat002)).Should().Be(antes, "SC-004");

        // Status final: a API recusa o cancelamento, sem mexer no estoque (FR-010).
        var cancelamento = await MudarStatusAsync(admin, pedidoId, "Cancelado", "Tentativa");
        cancelamento.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(cancelamento)).Should().Be("PEDIDO_TRANSICAO_INVALIDA");

        // Reentrega do mesmo evento: o estoque não volta duas vezes (cenário 4).
        await RepublicarEstoqueReservadoAsync(mensageria, stringConexao, pedidoId);
        (await EsperarLogAsync(logs, 5005, Prazo)).Should().BeTrue();
        DoPedido(fila, pedidoId).Should().ContainSingle();
        (await PagamentosAsync(stringConexao, pedidoId)).Should().Be(1);
        (await EstoqueAsync(stringConexao, Cat001), await EstoqueAsync(stringConexao, Cat002)).Should().Be(antes);
    }

    [Fact]
    public async Task Recusa_devolve_o_estoque_de_produto_desativado_depois_da_reserva()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var antes = await EstoqueAsync(stringConexao, Cat001);

        // Reserva com o Worker só de estoque; o estoque.reservado dele não é usado (achado H1).
        var estoque = await Estoque.CenarioEstoque.WorkerProntoAsync(mensageria, stringConexao);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 3))).GetProperty("id").GetGuid();
        (await MensageriaTeste.EsperarAteAsync(() => ReservadoAsync(stringConexao, pedidoId), Prazo)).Should().BeTrue();
        await estoque.DisposeAsync();
        await BancoDadosTeste.ExecutarAsync(stringConexao, $"UPDATE Produtos SET Ativo = 0 WHERE Id = '{Cat001}'");

        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, taxaAprovacao: 0);
        await PublicarEstoqueReservadoAsync(mensageria, pedidoId);

        (await EsperarStatusAsync(stringConexao, pedidoId, "PagamentoRejeitado", Prazo)).Should().BeTrue();
        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(antes);
        (await BancoDadosTeste.ConsultarAsync<bool>(stringConexao, $"SELECT Ativo FROM Produtos WHERE Id = '{Cat001}'")).Should().BeFalse();
    }
}
