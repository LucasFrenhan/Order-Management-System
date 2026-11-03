using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Produtos;
using PedidoFulfillment.Application.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Produtos;

/// <summary>Validação dos corpos e parâmetros do catálogo (REQ-22, research R-10).</summary>
public sealed class ValidacaoCatalogoTestes
{
    /// <summary>Atributos e <see cref="IValidatableObject"/>, como o filtro da rota.</summary>
    private static List<ValidationResult> Validar(object dto)
    {
        var resultados = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), resultados, validateAllProperties: true);
        return resultados;
    }

    private static IEnumerable<string> Mensagens(object dto) => Validar(dto).Select(r => r.ErrorMessage!).Distinct();

    private static CadastrarProdutoRequestDTO Cadastro() => new() { Sku = "CAF-250", Nome = "Café torrado", Preco = 32.50m, EstoqueInicial = 40 };

    [Fact]
    public void Cadastro_valido_e_aparado()
    {
        var cadastro = Cadastro() with { Sku = "  CAF-250 ", Nome = " Café  ", Descricao = "   " };

        Validar(cadastro).Should().BeEmpty();
        (cadastro.Sku, cadastro.Nome).Should().Be(("CAF-250", "Café"));
        cadastro.Descricao.Should().BeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1000000.00")]
    [InlineData("10.123")]
    public void Preco_fora_da_regra_e_recusado(string preco)
    {
        var valor = decimal.Parse(preco, System.Globalization.CultureInfo.InvariantCulture);

        Mensagens(Cadastro() with { Preco = valor }).Should().Equal(MensagensCatalogo.Preco);
        Mensagens(new AlterarProdutoRequestDTO { Nome = "X", Preco = valor }).Should().Equal(MensagensCatalogo.Preco);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("999999.99")]
    public void Preco_nos_limites_e_aceito(string preco)
    {
        Validar(Cadastro() with { Preco = decimal.Parse(preco, System.Globalization.CultureInfo.InvariantCulture) }).Should().BeEmpty();
    }

    [Fact]
    public void Textos_obrigatorios_e_tamanhos()
    {
        Mensagens(Cadastro() with { Sku = " " }).Should().Equal(MensagensCatalogo.Sku);
        Mensagens(Cadastro() with { Sku = new string('S', 51) }).Should().Equal(MensagensCatalogo.Sku);
        Mensagens(Cadastro() with { Nome = new string('N', 201) }).Should().Equal(MensagensCatalogo.Nome);
        Mensagens(Cadastro() with { Descricao = new string('D', 1001) }).Should().Equal(MensagensCatalogo.Descricao);
        Mensagens(Cadastro() with { EstoqueInicial = -1 }).Should().Equal(MensagensCatalogo.EstoqueInicial);
        Validar(Cadastro() with { EstoqueInicial = 1_000_000_000 }).Should().BeEmpty();
    }

    [Fact]
    public void Varios_erros_voltam_juntos()
    {
        var membros = Validar(new CadastrarProdutoRequestDTO { Sku = "", Nome = "", Preco = 0, EstoqueInicial = -5 })
            .SelectMany(r => r.MemberNames).Distinct();

        membros.Should().BeEquivalentTo(["Sku", "Nome", "Preco", "EstoqueInicial"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_001)]
    [InlineData(-1_000_001)]
    public void Variacao_fora_da_regra_e_recusada(int variacao)
    {
        Mensagens(new AjustarEstoqueRequestDTO { Variacao = variacao, Motivo = "Teste" }).Should().Equal(MensagensCatalogo.Variacao);
    }

    [Fact]
    public void Motivo_e_obrigatorio_e_limitado()
    {
        Mensagens(new AjustarEstoqueRequestDTO { Variacao = 1, Motivo = "  " }).Should().Equal(MensagensCatalogo.Motivo);
        Mensagens(new AjustarEstoqueRequestDTO { Variacao = 1, Motivo = new string('m', 201) }).Should().Equal(MensagensCatalogo.Motivo);
        Validar(new AjustarEstoqueRequestDTO { Variacao = -1_000_000, Motivo = "Avaria" }).Should().BeEmpty();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("!!")]
    [InlineData("um dois tres quatro cinco seis sete oito nove dez onze")]
    public void Busca_invalida_e_recusada(string busca)
    {
        Mensagens(new ListarProdutosRequestDTO { Busca = busca }).Should().Equal(MensagensCatalogo.Busca);
    }

    [Fact]
    public void Busca_longa_demais_e_recusada()
    {
        Mensagens(new ListarProdutosRequestDTO { Busca = new string('a', 101) }).Should().Equal(MensagensCatalogo.Busca);
    }

    [Fact]
    public void Situacao_e_tamanho()
    {
        Mensagens(new ListarProdutosRequestDTO { Situacao = "Esgotados" }).Should().Equal(MensagensCatalogo.Situacao);
        Mensagens(new ListarProdutosRequestDTO { Situacao = "1" }).Should().Equal(MensagensCatalogo.Situacao);
        Mensagens(new ListarProdutosRequestDTO { Tamanho = "0" }).Should().Equal("O tamanho deve ser um número entre 1 e 100.");

        var valida = new ListarProdutosRequestDTO { Situacao = "inativos", Busca = "café" };
        Validar(valida).Should().BeEmpty();
        valida.SituacaoPedida().Should().Be(SituacaoProduto.Inativos);
        valida.TamanhoPagina().Should().Be(20);
    }
}
