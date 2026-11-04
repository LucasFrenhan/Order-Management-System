using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;
using static PedidoFulfillment.Testes.Integracao.Produtos.CenarioProdutos;

namespace PedidoFulfillment.Testes.Integracao.Produtos;

/// <summary>REQ-22, User Story 1: o Cliente encontra os produtos que pode pedir (SQL Server real).</summary>
public sealed class CatalogoClienteTestes(BancoDadosTeste banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static readonly string[] CamposDaOperacao = ["quantidadeEstoque", "ativo", "criadoEm", "atualizadoEm"];

    private static bool TemCamposDaOperacao(System.Text.Json.JsonElement item) =>
        CamposDaOperacao.All(c => item.TryGetProperty(c, out _));

    [Fact]
    public async Task Cliente_ve_so_ativos_pelo_nome_sem_quantidade_e_a_operacao_ve_tudo()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        var (cliente, _, _) = await ClienteAsync(api);
        // Só os produtos do teste: o catálogo inicial fica de fora pela situação.
        await BancoDadosTeste.ExecutarAsync(stringConexao, "UPDATE Produtos SET Ativo = 0");
        var zebra = (await CadastradoAsync(admin, "Z-1", "Zebra de pelúcia", estoque: 0)).GetProperty("id").GetGuid();
        await CadastradoAsync(admin, "A-2", "Abajur");
        await CadastradoAsync(admin, "A-1", "Abajur");
        var inativo = (await CadastradoAsync(admin, "M-1", "Mesa")).GetProperty("id").GetGuid();
        (await DesativarAsync(admin, inativo)).StatusCode.Should().Be(HttpStatusCode.OK);

        var doCliente = await PaginaProdutosAsync(cliente);
        var daOperacao = await PaginaProdutosAsync(await OperadorAsync(api), "situacao=Todos&tamanho=100");

        Skus(doCliente).Should().Equal("A-1", "A-2", "Z-1");
        doCliente.GetProperty("itens").EnumerateArray().SelectMany(i => i.EnumerateObject()).Select(p => p.Name)
            .Should().NotContain(CamposDaOperacao);
        doCliente.GetProperty("itens")[2].GetProperty("disponivel").GetBoolean().Should().BeFalse();
        Skus(daOperacao).Should().Contain(["A-1", "A-2", "M-1", "Z-1"]);
        daOperacao.GetProperty("itens").EnumerateArray().Should().OnlyContain(i => TemCamposDaOperacao(i));

        var inativoParaCliente = await cliente.GetAsync($"{RotaProdutos}/{inativo}", Cancelamento);
        var inexistente = await cliente.GetAsync($"{RotaProdutos}/{Guid.NewGuid()}", Cancelamento);
        inativoParaCliente.StatusCode.Should().Be(HttpStatusCode.NotFound);
        inexistente.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CorpoAsync(inativoParaCliente)).GetProperty("detail").GetString().Should().Be((await CorpoAsync(inexistente)).GetProperty("detail").GetString());
        (await cliente.GetAsync($"{RotaProdutos}/{zebra}", Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Navegacao_de_45_produtos_nos_dois_sentidos()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        var (cliente, _, _) = await ClienteAsync(api);
        await BancoDadosTeste.ExecutarAsync(stringConexao, "UPDATE Produtos SET Ativo = 0");
        for (var i = 1; i <= 45; i++)
        {
            await CadastradoAsync(admin, $"NAV-{i:D3}", $"Produto {i:D3}");
        }

        var p1 = await PaginaProdutosAsync(cliente, "tamanho=20");
        var p2 = await PaginaProdutosAsync(cliente, $"tamanho=20&cursor={Uri.EscapeDataString(ProximoProdutos(p1)!)}");
        var p3 = await PaginaProdutosAsync(cliente, $"tamanho=20&cursor={Uri.EscapeDataString(ProximoProdutos(p2)!)}");
        var volta = await PaginaProdutosAsync(cliente, $"tamanho=20&cursor={Uri.EscapeDataString(AnteriorProdutos(p3)!)}");

        (Skus(p1).Count, Skus(p2).Count, Skus(p3).Count).Should().Be((20, 20, 5));
        Skus(p1).Concat(Skus(p2)).Concat(Skus(p3)).Should().Equal(Enumerable.Range(1, 45).Select(i => $"NAV-{i:D3}"));
        (AnteriorProdutos(p1), ProximoProdutos(p3)).Should().Be((null, null));
        Skus(volta).Should().Equal(Skus(p2));
    }

    [Fact]
    public async Task Produto_da_listagem_cria_pedido_com_o_preco_exibido_e_o_catalogo_inicial_entra_na_busca()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using (var api = Api(stringConexao))
        {
            var admin = await AdminAsync(api);
            var (cliente, _, _) = await ClienteAsync(api);
            var achado = (await BuscarAteAsync(cliente, "cat 001", skus => skus.Contains("CAT-001"))).Should().Contain("CAT-001").And.Subject;
            achado.Should().ContainSingle("o número vai por igualdade");
            var item = (await PaginaProdutosAsync(cliente, Busca("cat 001"))).GetProperty("itens")[0];
            var (id, preco) = (item.GetProperty("id").GetGuid(), item.GetProperty("preco").GetDecimal());

            var pedido = await CriadoAsync(cliente, (id, 2));

            pedido.GetProperty("itens")[0].GetProperty("precoUnitario").GetDecimal().Should().Be(preco, "SC-001");
            (await admin.PutAsJsonAsync($"{RotaProdutos}/{id}", new { nome = "Produto inicial alterado", preco = 1.5m }, Cancelamento))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Nova partida da API (Testes): o catálogo inicial não sobrescreve o que a API alterou (FR-023).
        await using var outra = Api(stringConexao);
        var produto = await CorpoAsync(await (await AdminAsync(outra)).GetAsync($"{RotaProdutos}/{Cat001}", Cancelamento));
        (produto.GetProperty("nome").GetString(), produto.GetProperty("preco").GetDecimal()).Should().Be(("Produto inicial alterado", 1.5m));
    }
}
