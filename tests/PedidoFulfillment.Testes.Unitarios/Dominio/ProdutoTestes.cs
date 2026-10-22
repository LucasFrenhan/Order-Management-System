using FluentAssertions;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Dominio;

public sealed class ProdutoTestes
{
    private static readonly DateTimeOffset Instante = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static Produto NovoProduto(decimal preco = 10m, int estoque = 10) =>
        new("  SKU-1 ", "  Teclado  ", null, preco, estoque, ativo: true, Instante);

    [Fact]
    public void Construcao_apara_textos_e_preenche_datas()
    {
        var produto = NovoProduto();

        produto.Id.Should().NotBeEmpty();
        produto.Id.Version.Should().Be(7);
        produto.Sku.Should().Be("SKU-1");
        produto.Nome.Should().Be("Teclado");
        produto.Descricao.Should().BeNull();
        produto.Ativo.Should().BeTrue();
        produto.CriadoEm.Should().Be(Instante);
        produto.AtualizadoEm.Should().Be(Instante);
    }

    [Fact]
    public void Construcao_aceita_id_informado()
    {
        var id = Guid.Parse("0192b6a0-0000-7000-8000-000000000001");

        new Produto("CAT-001", "Teclado", null, 1m, 1, true, Instante, id).Id.Should().Be(id);
    }

    [Theory]
    [InlineData("", "Nome")]
    [InlineData("   ", "Nome")]
    [InlineData("SKU", "")]
    [InlineData("SKU", "   ")]
    public void Construcao_recusa_sku_ou_nome_vazio(string sku, string nome)
    {
        var criar = () => new Produto(sku, nome, null, 1m, 1, true, Instante);

        criar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construcao_recusa_textos_acima_do_limite()
    {
        new Func<Produto>(() => new Produto(new string('s', 51), "Nome", null, 1m, 1, true, Instante)).Should().Throw<ArgumentException>();
        new Func<Produto>(() => new Produto("SKU", new string('n', 201), null, 1m, 1, true, Instante)).Should().Throw<ArgumentException>();
        new Func<Produto>(() => new Produto("SKU", "Nome", new string('d', 1001), 1m, 1, true, Instante)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Construcao_recusa_preco_ou_estoque_negativo()
    {
        new Func<Produto>(() => NovoProduto(preco: -0.01m)).Should().Throw<ArgumentOutOfRangeException>();
        new Func<Produto>(() => NovoProduto(estoque: -1)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Construcao_recusa_instante_fora_de_utc()
    {
        var criar = () => new Produto("SKU", "Nome", null, 1m, 1, true, new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.FromHours(-3)));

        criar.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("10.005", "10.00")]
    [InlineData("10.015", "10.02")]
    [InlineData("10.5", "10.50")]
    public void Preco_e_arredondado_para_duas_casas(string informado, string esperado)
    {
        NovoProduto(preco: decimal.Parse(informado, System.Globalization.CultureInfo.InvariantCulture))
            .Preco.Should().Be(decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void AjustarEstoque_soma_a_variacao_e_atualiza_a_data()
    {
        var produto = NovoProduto(estoque: 10);
        var depois = Instante.AddMinutes(1);

        produto.AjustarEstoque(5, depois);
        produto.AjustarEstoque(-3, depois);

        produto.QuantidadeEstoque.Should().Be(12);
        produto.AtualizadoEm.Should().Be(depois);
    }

    [Fact]
    public void AjustarEstoque_que_deixaria_negativo_e_recusado_sem_alterar_nada()
    {
        var produto = NovoProduto(estoque: 2);

        var ajustar = () => produto.AjustarEstoque(-3, Instante.AddMinutes(1));

        ajustar.Should().Throw<InvalidOperationException>();
        produto.QuantidadeEstoque.Should().Be(2);
        produto.AtualizadoEm.Should().Be(Instante);
    }

    [Fact]
    public void AlterarDados_ativar_e_desativar_alteram_os_campos_e_a_data()
    {
        var produto = NovoProduto();
        var depois = Instante.AddMinutes(1);

        produto.AlterarDados(" Teclado sem fio ", "Descrição", 20.555m, depois);
        produto.Desativar(depois);

        produto.Nome.Should().Be("Teclado sem fio");
        produto.Descricao.Should().Be("Descrição");
        produto.Preco.Should().Be(20.56m);
        produto.Ativo.Should().BeFalse();
        produto.AtualizadoEm.Should().Be(depois);

        produto.Ativar(depois.AddMinutes(1));

        produto.Ativo.Should().BeTrue();
        produto.AtualizadoEm.Should().Be(depois.AddMinutes(1));
    }
}
