using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;
using static PedidoFulfillment.Testes.Integracao.Produtos.CenarioProdutos;

namespace PedidoFulfillment.Testes.Integracao.Produtos;

/// <summary>
/// REQ-22, User Story 1: busca em português pelo Full-Text Search, com os casos do teste prático
/// (research R-01). O índice é atualizado em segundo plano: cada busca espera por condição, até
/// 10 s (FR-007a).
/// </summary>
public sealed class BuscaCatalogoTestes(BancoDadosTeste banco)
{
    private static readonly (string Texto, string[] Esperado)[] Casos =
    [
        ("cafe", ["BUS-002"]),
        ("camisetas", ["BUS-001"]),
        ("calcas", ["BUS-003"]),
        ("lencol", ["BUS-005"]),
        ("papel", ["BUS-008"]),
        ("pães", ["BUS-004"]),
        ("limões", ["BUS-007"]),
        ("cami", ["BUS-001"]),
        ("algodao", ["BUS-001"]),
        ("bus 003", ["BUS-003"]),
        ("camiseta algodao", ["BUS-001"]),
        ("camiseta inox", []),
        ("\"cafe\" OR 1=1", []),
        ("cafe*", ["BUS-002"])
    ];

    [Fact]
    public async Task Busca_em_portugues_acha_por_palavras_flexoes_prefixo_descricao_e_sku()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        var (cliente, _, _) = await ClienteAsync(api);
        var produtos = new (string Sku, string Nome, string Descricao)[]
        {
            ("BUS-001", "Camiseta básica", "Algodão orgânico"),
            ("BUS-002", "Café torrado", "Grãos especiais"),
            ("BUS-003", "Calças jeans", "Tecido resistente"),
            ("BUS-004", "Pão de forma", "Integral"),
            ("BUS-005", "Lençóis de casal", "Percal 200 fios"),
            ("BUS-006", "Mochila escolar", "Com rodinhas"),
            ("BUS-007", "Limões sicilianos", "Caixa com 12"),
            ("BUS-008", "Papéis coloridos", "Pacote")
        };
        foreach (var (sku, nome, descricao) in produtos)
        {
            await CadastradoAsync(admin, sku, nome, descricao: descricao);
        }

        // Cada produto já aparece pelo nome: o índice está em dia. Esperar só o último não basta,
        // porque o Full-Text Search processa as mudanças em lote, sem ordem garantida.
        foreach (var (sku, nome, _) in produtos)
        {
            (await BuscarAteAsync(cliente, nome, skus => skus.Contains(sku))).Should().Contain(sku, $"'{nome}' deveria estar indexado");
        }

        var erros = new List<string>();
        foreach (var (texto, esperado) in Casos)
        {
            var achados = Skus(await PaginaProdutosAsync(cliente, Busca(texto))).Where(s => s.StartsWith("BUS-", StringComparison.Ordinal)).ToList();
            if (!achados.SequenceEqual(esperado))
            {
                erros.Add($"'{texto}': esperado [{string.Join(", ", esperado)}], veio [{string.Join(", ", achados)}]");
            }
        }

        erros.Should().BeEmpty();
    }

    [Fact]
    public async Task Busca_combina_com_o_cursor_e_o_desativado_some_na_hora()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        var (cliente, _, _) = await ClienteAsync(api);
        var ids = new List<Guid>();
        for (var i = 1; i <= 5; i++)
        {
            ids.Add((await CadastradoAsync(admin, $"GAR-{i}", $"Garrafa térmica {i}")).GetProperty("id").GetGuid());
        }

        (await BuscarAteAsync(cliente, "garrafas", skus => skus.Count == 5)).Should().HaveCount(5);
        var p1 = await PaginaProdutosAsync(cliente, $"{Busca("garrafas")}&tamanho=3");
        var p2 = await PaginaProdutosAsync(cliente, $"{Busca("garrafas")}&tamanho=3&cursor={Uri.EscapeDataString(ProximoProdutos(p1)!)}");
        Skus(p1).Concat(Skus(p2)).Should().Equal("GAR-1", "GAR-2", "GAR-3", "GAR-4", "GAR-5");

        // Cursor de uma busca usado com outra: 400.
        (await ListarProdutosAsync(cliente, $"{Busca("termica")}&tamanho=3&cursor={Uri.EscapeDataString(ProximoProdutos(p1)!)}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await DesativarAsync(admin, ids[0])).StatusCode.Should().Be(HttpStatusCode.OK);
        Skus(await PaginaProdutosAsync(cliente, Busca("garrafas"))).Should().NotContain("GAR-1", "SC-006: o desativado some na hora");
    }
}
