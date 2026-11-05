using FluentAssertions;
using PedidoFulfillment.Application.Emails;

namespace PedidoFulfillment.Testes.Unitarios.Emails;

/// <summary>Os textos dos e-mails (REQ-24).</summary>
public sealed class ModelosEmailTestes
{
    private const string Destino = "maria@exemplo.com.br";
    private const string Token = "abc-123_token";

    private static readonly TimeSpan Meia = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Dia = TimeSpan.FromHours(24);

    [Fact]
    public void Recuperacao_sem_endereco_de_aplicacao_traz_so_o_token()
    {
        var mensagem = ModelosEmail.Recuperacao(Destino, Token, Meia, null);

        mensagem.Destino.Should().Be(Destino);
        mensagem.Assunto.Should().Be("Recuperação de senha");
        mensagem.Corpo.Should().Contain($"Token: {Token}").And.Contain("30 minutos").And.NotContain("http");
    }

    [Fact]
    public void Recuperacao_com_endereco_de_aplicacao_traz_o_link()
    {
        var mensagem = ModelosEmail.Recuperacao(Destino, Token, Meia, "https://app.exemplo.com.br/");

        mensagem.Corpo.Should().Contain($"https://app.exemplo.com.br/redefinir-senha?token={Token}").And.Contain($"Token: {Token}");
    }

    [Fact]
    public void Confirmacao_traz_o_link_e_a_validade_em_horas()
    {
        var mensagem = ModelosEmail.Confirmacao(Destino, Token, Dia, "https://app.exemplo.com.br");

        mensagem.Assunto.Should().Be("Confirmação de e-mail");
        mensagem.Corpo.Should().Contain($"https://app.exemplo.com.br/confirmar-email?token={Token}").And.Contain("24 horas");
    }

    [Fact]
    public void O_token_e_codificado_na_url() =>
        ModelosEmail.Recuperacao(Destino, "a b&c", Meia, "https://app").Corpo.Should().Contain("token=a%20b%26c");

    [Fact]
    public void O_aviso_nao_traz_token()
    {
        var mensagem = ModelosEmail.AvisoSenhaRedefinida(Destino);

        mensagem.Assunto.Should().Be("Sua senha foi redefinida");
        mensagem.Corpo.Should().Contain("redefinida").And.NotContain("Token");
    }

    [Fact]
    public void A_descricao_da_mensagem_nao_mostra_o_corpo() =>
        ModelosEmail.Recuperacao(Destino, Token, Meia, null).ToString().Should().NotContain(Token).And.NotContain(Destino);
}
