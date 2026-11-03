using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Estoque;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;
using static PedidoFulfillment.Testes.Integracao.Produtos.CenarioProdutos;

namespace PedidoFulfillment.Testes.Integracao.Produtos;

/// <summary>REQ-22, User Story 4: desativação sem apagar e reativação (SQL Server real).</summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class AtivacaoProdutosTestes(BancoDadosTeste banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Desativado_some_do_cliente_nao_pode_ser_pedido_e_pedidos_antigos_continuam()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        var operador = await OperadorAsync(api);
        var (cliente, _, _) = await ClienteAsync(api);
        var produtoId = (await CadastradoAsync(admin, "ATV-1", "Luminária de mesa", 80m, 10)).GetProperty("id").GetGuid();
        var antigo = (await CriadoAsync(cliente, (produtoId, 3))).GetProperty("id").GetGuid();
        await DisputaEstoqueTestes.ReservarComoABaseAsync(api.Services, antigo);
        (await BuscarAteAsync(cliente, "luminarias", skus => skus.Contains("ATV-1"))).Should().Contain("ATV-1");

        var desativado = await DesativarAsync(admin, produtoId);
        var repetido = await DesativarAsync(admin, produtoId);

        (desativado.StatusCode, repetido.StatusCode).Should().Be((HttpStatusCode.OK, HttpStatusCode.OK));
        (await CorpoAsync(repetido)).GetProperty("ativo").GetBoolean().Should().BeFalse();
        Skus(await PaginaProdutosAsync(cliente)).Should().NotContain("ATV-1");
        Skus(await PaginaProdutosAsync(cliente, Busca("luminarias"))).Should().NotContain("ATV-1");
        (await cliente.GetAsync($"{RotaProdutos}/{produtoId}", Cancelamento)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CorpoAsync(await operador.GetAsync($"{RotaProdutos}/{produtoId}", Cancelamento))).GetProperty("ativo").GetBoolean().Should().BeFalse();

        var pedidoNovo = await CriarAsync(cliente, Guid.NewGuid(), (produtoId, 1));
        pedidoNovo.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodigoAsync(pedidoNovo)).Should().Be("PRODUTO_INDISPONIVEL");

        var antigoAgora = await CorpoAsync(await cliente.GetAsync($"{Rota}/{antigo}", Cancelamento));
        antigoAgora.GetProperty("itens")[0].GetProperty("precoUnitario").GetDecimal().Should().Be(80m);

        // Cancelar o pedido antigo devolve o estoque ao produto inativo.
        (await MudarStatusAsync(cliente, antigo, "Cancelado", "Desisti.")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await EstoqueAsync(stringConexao, produtoId)).Should().Be(10);

        (await AtivarAsync(admin, produtoId)).StatusCode.Should().Be(HttpStatusCode.OK);
        Skus(await PaginaProdutosAsync(cliente)).Should().Contain("ATV-1");
        (await CriarAsync(cliente, Guid.NewGuid(), (produtoId, 1))).StatusCode.Should().Be(HttpStatusCode.Created);
    }
}
