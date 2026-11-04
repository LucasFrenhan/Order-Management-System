using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Endpoints;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;
using static PedidoFulfillment.Testes.Integracao.Produtos.CenarioProdutos;

namespace PedidoFulfillment.Testes.Integracao.Produtos;

/// <summary>REQ-22, User Story 2: cadastro, SKU único e preço novo só para pedidos novos.</summary>
public sealed class CadastroProdutosTestes(BancoDadosTeste banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cadastro_valido_e_invalido_e_sku_repetido_com_outra_caixa()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var admin = await AdminAsync(api);

        var resposta = await CadastrarAsync(admin, "CAF-250", "Café torrado 250 g", 32.50m, 40, "Grãos especiais");
        var corpo = await CorpoAsync(resposta);
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        resposta.Headers.Location!.ToString().Should().Be($"/api/v1/produtos/{corpo.GetProperty("id").GetGuid()}");
        (corpo.GetProperty("quantidadeEstoque").GetInt32(), corpo.GetProperty("ativo").GetBoolean()).Should().Be((40, true));

        var repetido = await CadastrarAsync(admin, "caf-250", "Outro");
        repetido.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(repetido)).Should().Be("PRODUTO_SKU_DUPLICADO");

        var invalido = await admin.PostAsJsonAsync(RotaProdutos, new { sku = "", nome = "", preco = 0, estoqueInicial = -1 }, Cancelamento);
        (await EndpointsAutenticacaoTestes.ErrosDe(invalido, RotaProdutos)).Keys.Should().BeEquivalentTo(["sku", "nome", "preco", "estoqueInicial"]);
    }

    [Fact]
    public async Task Dez_cadastros_simultaneos_do_mesmo_sku_criam_um_so_produto()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => CadastrarAsync(admin, "DISPUTA-1", $"Produto {i}")));

        respostas.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(9);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM Produtos WHERE Sku = 'DISPUTA-1'")).Should().Be(1);
    }

    [Fact]
    public async Task Preco_novo_vale_so_para_pedidos_novos_e_o_renomeado_entra_na_busca()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var admin = await AdminAsync(api);
        var (cliente, _, _) = await ClienteAsync(api);
        var id = (await CadastradoAsync(admin, "PRC-1", "Caneca", 20m, 10)).GetProperty("id").GetGuid();
        var antigo = await CriadoAsync(cliente, (id, 2));

        var alteracao = await admin.PutAsJsonAsync($"{RotaProdutos}/{id}", new { nome = "Xícara de porcelana", preco = 25m }, Cancelamento);
        alteracao.StatusCode.Should().Be(HttpStatusCode.OK);
        var novo = await CriadoAsync(cliente, (id, 2));

        var antigoAgora = await CorpoAsync(await cliente.GetAsync($"{Rota}/{antigo.GetProperty("id").GetGuid()}", Cancelamento));
        var itemAntigo = antigoAgora.GetProperty("itens")[0];
        (itemAntigo.GetProperty("precoUnitario").GetDecimal(), itemAntigo.GetProperty("subtotal").GetDecimal(), antigoAgora.GetProperty("valorTotal").GetDecimal())
            .Should().Be((20m, 40m, 40m), "SC-003");
        novo.GetProperty("itens")[0].GetProperty("precoUnitario").GetDecimal().Should().Be(25m);
        (await BuscarAteAsync(admin, "xícaras porcelana", skus => skus.Contains("PRC-1"))).Should().Contain("PRC-1");
    }
}
