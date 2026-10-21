using FluentAssertions;
using PedidoFulfillment.Application.Autenticacao;

namespace PedidoFulfillment.Testes.Unitarios.Autenticacao;

public sealed class MascaraEmailTestes
{
    [Theory]
    [InlineData("maria@exemplo.com.br", "m***@exemplo.com.br")]
    [InlineData("Maria.Silva@Exemplo.com.br", "M***@Exemplo.com.br")]
    [InlineData("m@exemplo.com.br", "m***@exemplo.com.br")]
    public void Mantem_so_a_primeira_letra_e_o_dominio(string email, string esperado)
    {
        MascaraEmail.Mascarar(email).Should().Be(esperado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sem-arroba")]
    [InlineData("@exemplo.com.br")]
    [InlineData("maria@")]
    [InlineData("maria@a@b.com")]
    public void Valor_fora_do_formato_vira_so_asteriscos(string? email)
    {
        MascaraEmail.Mascarar(email).Should().Be("***");
    }

    [Fact]
    public void Nunca_contem_a_parte_local_completa()
    {
        MascaraEmail.Mascarar("joaozinho@exemplo.com.br").Should().NotContain("joaozinho");
    }
}
