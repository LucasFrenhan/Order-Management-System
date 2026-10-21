using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Autenticacao;

namespace PedidoFulfillment.Testes.Unitarios.Contratos;

public sealed class RegistrarUsuarioRequestDTOTestes
{
    private static RegistrarUsuarioRequestDTO Valido() => new()
    {
        Nome = "Maria Silva",
        Email = "maria.silva@exemplo.com.br",
        Senha = "SenhaForte123"
    };

    [Fact]
    public void Dto_valido_nao_tem_erros()
    {
        Validacao.Validar(Valido()).Should().BeEmpty();
    }

    [Fact]
    public void Campos_obrigatorios_ausentes_geram_erros_em_portugues()
    {
        var erros = Validacao.Validar(new RegistrarUsuarioRequestDTO());

        erros.Should().ContainKeys("Nome", "Email", "Senha");
        erros["Nome"].Should().Contain("O nome é obrigatório.");
        erros["Email"].Should().Contain("O e-mail é obrigatório.");
        erros["Senha"].Should().Contain("A senha é obrigatória.");
    }

    [Theory]
    [InlineData("M")]
    [InlineData("   M   ")]
    [InlineData("   ")]
    public void Nome_com_menos_de_2_caracteres_apos_aparar_e_invalido(string nome)
    {
        var erros = Validacao.Validar(Valido() with { Nome = nome });

        erros.Should().ContainKey("Nome");
    }

    [Fact]
    public void Nome_com_mais_de_100_caracteres_e_invalido()
    {
        Validacao.Validar(Valido() with { Nome = new string('a', 101) }).Should().ContainKey("Nome");
    }

    [Fact]
    public void Nome_com_espacos_nas_pontas_e_conteudo_valido_e_aceito()
    {
        Validacao.Validar(Valido() with { Nome = "  Ana  " }).Should().BeEmpty();
    }

    [Theory]
    [InlineData("sem-arroba")]
    [InlineData("a@")]
    public void Email_em_formato_invalido_e_rejeitado(string email)
    {
        Validacao.Validar(Valido() with { Email = email })["Email"].Should().Contain("O e-mail deve ter um formato válido.");
    }

    [Fact]
    public void Email_com_mais_de_256_caracteres_e_rejeitado()
    {
        var email = new string('a', 250) + "@ex.com";

        Validacao.Validar(Valido() with { Email = email }).Should().ContainKey("Email");
    }

    [Theory]
    [InlineData("Curta1A")]
    [InlineData("semmaiuscula1")]
    [InlineData("SEMMINUSCULA1")]
    [InlineData("SemDigitoAqui")]
    public void Senha_fora_da_politica_e_rejeitada(string senha)
    {
        Validacao.Validar(Valido() with { Senha = senha }).Should().ContainKey("Senha");
    }

    [Fact]
    public void Senha_com_mais_de_128_caracteres_e_rejeitada()
    {
        Validacao.Validar(Valido() with { Senha = "Aa1" + new string('x', 126) }).Should().ContainKey("Senha");
    }
}
