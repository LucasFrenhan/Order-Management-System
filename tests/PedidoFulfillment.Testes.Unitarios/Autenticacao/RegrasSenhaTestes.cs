using FluentAssertions;
using PedidoFulfillment.Application.Autenticacao;

namespace PedidoFulfillment.Testes.Unitarios.Autenticacao;

/// <summary>A regra de composição, igual no registro, na troca e na redefinição (REQ-24, R-09).</summary>
public sealed class RegrasSenhaTestes
{
    [Theory]
    [InlineData("senhaforte1")]
    [InlineData("SENHAFORTE1")]
    [InlineData("SenhaForte")]
    public void Sem_maiuscula_minuscula_ou_digito_nao_atende(string senha) =>
        RegrasSenha.AtendeComposicao(senha).Should().BeFalse();

    [Fact]
    public void Senha_valida_atende() =>
        RegrasSenha.AtendeComposicao("SenhaForte1").Should().BeTrue();

    [Fact]
    public void As_mensagens_sao_as_do_contrato_v1()
    {
        RegrasSenha.MensagemComposicao.Should().Be("A senha deve ter ao menos uma letra maiúscula, uma letra minúscula e um dígito.");
        RegrasSenha.MensagemTamanho.Should().Be("A senha deve ter entre 8 e 128 caracteres.");
    }
}
