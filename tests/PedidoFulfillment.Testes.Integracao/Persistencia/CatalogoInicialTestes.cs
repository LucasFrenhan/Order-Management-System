using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>
/// User Story 4, cenários 5 a 7: catálogo inicial só em Development e Testes, idempotente
/// (FR-020, <c>contracts/catalogo-inicial.md</c>).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class CatalogoInicialTestes(BancoDadosTeste banco)
{
    private const string ContarProdutos = "SELECT COUNT(*) FROM Produtos";

    /// <summary>A tabela do contrato: Id, Sku, Preço, Estoque e Ativo.</summary>
    private static readonly (string Id, string Sku, decimal Preco, int Estoque, bool Ativo)[] Esperados =
    [
        ("0192b6a0-0000-7000-8000-000000000001", "CAT-001", 349.90m, 50, true),
        ("0192b6a0-0000-7000-8000-000000000002", "CAT-002", 129.90m, 100, true),
        ("0192b6a0-0000-7000-8000-000000000003", "CAT-003", 1899.00m, 20, true),
        ("0192b6a0-0000-7000-8000-000000000004", "CAT-004", 259.90m, 40, true),
        ("0192b6a0-0000-7000-8000-000000000005", "CAT-005", 219.90m, 30, true),
        ("0192b6a0-0000-7000-8000-000000000006", "CAT-006", 39.90m, 500, true),
        ("0192b6a0-0000-7000-8000-000000000007", "CAT-007", 89.90m, 60, true),
        ("0192b6a0-0000-7000-8000-000000000008", "CAT-008", 199.90m, 1, true),
        ("0192b6a0-0000-7000-8000-000000000009", "CAT-009", 1299.00m, 0, true),
        ("0192b6a0-0000-7000-8000-000000000010", "CAT-010", 59.90m, 80, false)
    ];

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Banco_vazio_recebe_os_dez_produtos_do_contrato()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();

        Iniciar(stringConexao);

        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var produtos = await contexto.Produtos.OrderBy(p => p.Sku).ToListAsync(Cancelamento);
        produtos.Select(p => (p.Id.ToString(), p.Sku, p.Preco, p.QuantidadeEstoque, p.Ativo)).Should().Equal(Esperados);
    }

    [Fact]
    public async Task Reiniciar_mantem_alteracoes_e_recria_so_o_que_falta()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        Iniciar(stringConexao);
        await BancoDadosTeste.ExecutarAsync(stringConexao, "UPDATE Produtos SET QuantidadeEstoque = 7 WHERE Sku = 'CAT-001'");
        await BancoDadosTeste.ExecutarAsync(stringConexao, "DELETE FROM Produtos WHERE Sku = 'CAT-002'");

        Iniciar(stringConexao);

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarProdutos)).Should().Be(10);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT QuantidadeEstoque FROM Produtos WHERE Sku = 'CAT-001'")).Should().Be(7);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM Produtos WHERE Sku = 'CAT-002'")).Should().Be(1);
    }

    [Fact]
    public async Task Duas_instancias_iniciando_juntas_deixam_dez_produtos()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();

        await Task.WhenAll(Task.Run(() => Iniciar(stringConexao), Cancelamento), Task.Run(() => Iniciar(stringConexao), Cancelamento));

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarProdutos)).Should().Be(10);
    }

    [Fact]
    public async Task Fora_de_development_e_testes_nenhum_produto_e_criado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();

        Iniciar(stringConexao, "Production");

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarProdutos)).Should().Be(0);
    }

    [Fact]
    public async Task Com_a_preparacao_desligada_nenhum_produto_e_criado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();

        Iniciar(stringConexao, preparar: false);

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarProdutos)).Should().Be(0);
    }

    private static void Iniciar(string stringConexao, string ambiente = "Testes", bool preparar = true)
    {
        using var fabrica = new FabricaApi { Ambiente = ambiente, PrepararBanco = preparar, StringConexao = stringConexao };
        fabrica.CreateClient().Dispose();
    }
}
