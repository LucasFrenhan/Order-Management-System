using FluentAssertions;
using PedidoFulfillment.Application.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Produtos;

/// <summary>Palavras e condição da busca do catálogo (REQ-22, research R-04).</summary>
public sealed class FiltroListagemProdutosTestes
{
    private static FiltroListagemProdutos Filtro(string busca, SituacaoProduto situacao = SituacaoProduto.Todos) =>
        new() { Palavras = FiltroListagemProdutos.ExtrairPalavras(busca), Situacao = situacao };

    [Fact]
    public void Palavras_sao_letras_e_digitos_em_minusculas_sem_repeticao()
    {
        FiltroListagemProdutos.ExtrairPalavras("  Café   TORRADO café, 250g! ").Should().Equal("café", "torrado", "250g");
        FiltroListagemProdutos.ExtrairPalavras(null).Should().BeEmpty();
        FiltroListagemProdutos.ExtrairPalavras("!! -- **").Should().BeEmpty();
    }

    [Fact]
    public void Cada_palavra_vira_flexoes_ou_prefixo_e_todas_sao_obrigatorias()
    {
        Filtro("cafe torrado").CondicaoFullText().Should().Be(
            "(FORMSOF(INFLECTIONAL, \"cafe\") OR \"cafe*\") AND (FORMSOF(INFLECTIONAL, \"torrado\") OR \"torrado*\")");
    }

    [Fact]
    public void Numeros_vao_so_por_igualdade()
    {
        Filtro("CAT-001").CondicaoFullText().Should().Be("(FORMSOF(INFLECTIONAL, \"cat\") OR \"cat*\") AND \"001\"");
    }

    [Theory]
    [InlineData("\"cafe\" OR 1=1")]
    [InlineData("cafe*) OR (\"x")]
    [InlineData("NEAR(cafe, torrado)")]
    [InlineData("cafe & !torrado | ~x")]
    public void Sintaxe_do_full_text_nunca_chega_a_condicao(string busca)
    {
        var condicao = Filtro(busca).CondicaoFullText();

        // Fora dos termos gerados, só sobram os operadores que o próprio filtro escreve.
        var semTermos = System.Text.RegularExpressions.Regex.Replace(condicao, "\"[\\p{L}\\p{N}]+\\*?\"", "T");
        semTermos.Replace("FORMSOF(INFLECTIONAL, T)", "F").Replace(" OR ", "|").Replace(" AND ", "&")
            .Should().MatchRegex(@"^[\(\)F|T&]+$");
        condicao.Should().NotContain("NEAR(").And.NotContain("1=1").And.NotContain("!").And.NotContain("~");
    }

    [Fact]
    public void Palavras_do_operador_sao_tratadas_como_texto()
    {
        Filtro("cafe or torrado").CondicaoFullText().Should().Contain("\"or\"").And.Contain("\"or*\"");
    }

    [Fact]
    public void Texto_canonico_ignora_caixa_espacos_ordem_e_repeticao()
    {
        Filtro("Café torrado").TextoCanonico().Should().Be(Filtro("  torrado  CAFÉ café ").TextoCanonico());
        Filtro("cafe").TextoCanonico().Should().NotBe(Filtro("cafe", SituacaoProduto.Ativos).TextoCanonico());
    }

    [Fact]
    public void Nomes_usados_nao_trazem_os_valores()
    {
        Filtro("cafe", SituacaoProduto.Inativos).NomesUsados().Should().Be("busca,situacao");
        Filtro("").NomesUsados().Should().Be("nenhum");
    }
}
