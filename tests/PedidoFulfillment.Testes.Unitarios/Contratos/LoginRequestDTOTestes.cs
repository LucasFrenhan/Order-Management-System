using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Autenticacao;

namespace PedidoFulfillment.Testes.Unitarios.Contratos;

public sealed class LoginRequestDTOTestes
{
    [Fact]
    public void Dto_valido_nao_tem_erros()
    {
        Validacao.Validar(new LoginRequestDTO { Email = "maria@exemplo.com.br", Senha = "qualquer" }).Should().BeEmpty();
    }

    [Fact]
    public void Email_e_senha_sao_obrigatorios()
    {
        var erros = Validacao.Validar(new LoginRequestDTO());

        erros["Email"].Should().Contain("O e-mail é obrigatório.");
        erros["Senha"].Should().Contain("A senha é obrigatória.");
    }

    [Fact]
    public void Email_em_formato_invalido_e_rejeitado()
    {
        Validacao.Validar(new LoginRequestDTO { Email = "invalido", Senha = "x" }).Should().ContainKey("Email");
    }
}
