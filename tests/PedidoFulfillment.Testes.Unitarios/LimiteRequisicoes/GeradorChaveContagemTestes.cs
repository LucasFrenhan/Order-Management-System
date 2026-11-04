using FluentAssertions;
using PedidoFulfillment.Application.LimiteRequisicoes;

namespace PedidoFulfillment.Testes.Unitarios.LimiteRequisicoes;

/// <summary>A identidade da contagem em forma não legível (REQ-23, FR-003, FR-010, R-08).</summary>
public sealed class GeradorChaveContagemTestes
{
    private static readonly byte[] Chave = new byte[32];
    private static readonly byte[] OutraChave = [.. Enumerable.Repeat((byte)1, 32)];

    private static byte[] Gerar(string politica = "login_origem_email", string origem = "203.0.113.7", string? email = "maria@exemplo.com.br") =>
        GeradorChaveContagem.Gerar(Chave, politica, origem, email);

    [Fact]
    public void Tem_32_bytes_e_e_deterministica()
    {
        Gerar().Should().HaveCount(32);
        Gerar().Should().Equal(Gerar());
    }

    [Fact]
    public void Muda_com_a_politica_a_origem_o_email_e_a_chave()
    {
        Gerar(politica: "login_origem").Should().NotEqual(Gerar());
        Gerar(origem: "203.0.113.8").Should().NotEqual(Gerar());
        Gerar(email: "joao@exemplo.com.br").Should().NotEqual(Gerar());
        GeradorChaveContagem.Gerar(OutraChave, "login_origem_email", "203.0.113.7", "maria@exemplo.com.br").Should().NotEqual(Gerar());
    }

    [Theory]
    [InlineData("MARIA@EXEMPLO.COM.BR")]
    [InlineData("  Maria@Exemplo.com.br ")]
    public void Email_com_outra_caixa_ou_espacos_e_o_mesmo(string email) =>
        Gerar(email: email).Should().Equal(Gerar());

    [Fact]
    public void O_separador_impede_colisao_entre_origem_e_email() =>
        Gerar(origem: "a", email: "b@c").Should().NotEqual(Gerar(origem: "ab", email: "@c"));

    [Fact]
    public void Sem_email_e_diferente_de_qualquer_email() =>
        GeradorChaveContagem.Gerar(Chave, "login_origem", "203.0.113.7").Should().NotEqual(Gerar(politica: "login_origem"));
}
