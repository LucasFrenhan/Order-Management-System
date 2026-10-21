using FluentAssertions;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Testes.Unitarios.Configuracao;

public sealed class OpcoesUsuariosIniciaisTestes
{
    private static readonly ValidadorOpcoesUsuariosIniciais Validador = new();

    private static UsuarioInicial Valido() => new()
    {
        Nome = "Administrador",
        Email = "admin@oms.local",
        Senha = "AdminDev2026",
        Perfil = "Admin"
    };

    private static IReadOnlyList<string> Falhas(params UsuarioInicial[] usuarios)
    {
        var opcoes = new OpcoesUsuariosIniciais();
        opcoes.Usuarios.AddRange(usuarios);
        var resultado = Validador.Validate(null, opcoes);
        return resultado.Succeeded ? [] : resultado.Failures!.ToArray();
    }

    [Fact]
    public void Lista_vazia_e_valida()
    {
        Falhas().Should().BeEmpty();
    }

    [Fact]
    public void Usuarios_completos_sao_validos()
    {
        var operador = Valido();
        operador.Perfil = "OperadorLogistica";

        Falhas(Valido(), operador).Should().BeEmpty();
    }

    [Theory]
    [InlineData(nameof(UsuarioInicial.Nome))]
    [InlineData(nameof(UsuarioInicial.Email))]
    [InlineData(nameof(UsuarioInicial.Senha))]
    [InlineData(nameof(UsuarioInicial.Perfil))]
    public void Campo_ausente_falha_nomeando_a_chave(string campo)
    {
        var usuario = Valido();
        typeof(UsuarioInicial).GetProperty(campo)!.SetValue(usuario, " ");

        Falhas(Valido(), usuario).Should().Equal($"A configuração UsuariosIniciais:1:{campo} é obrigatória.");
    }

    [Theory]
    [InlineData("Cliente")]
    [InlineData("Gerente")]
    public void Perfil_fora_de_admin_e_operador_e_recusado(string perfil)
    {
        var usuario = Valido();
        usuario.Perfil = perfil;

        Falhas(usuario).Should().Equal("A configuração UsuariosIniciais:0:Perfil deve ser Admin ou OperadorLogistica.");
    }

    [Theory]
    [InlineData("semmaiuscula1")]
    [InlineData("Curta1")]
    public void Senha_fora_da_regra_do_contrato_e_recusada_sem_mostrar_o_valor(string senha)
    {
        var usuario = Valido();
        usuario.Senha = senha;

        var falhas = Falhas(usuario);

        falhas.Should().ContainSingle().Which.Should().StartWith("A configuração UsuariosIniciais:0:Senha é inválida:");
        falhas[0].Should().NotContain(senha);
    }

    [Fact]
    public void Senha_com_letras_acentuadas_aceita_pelo_contrato_e_aceita()
    {
        var usuario = Valido();
        usuario.Senha = "ÇÃOabc12";

        Falhas(usuario).Should().BeEmpty();
    }

    [Fact]
    public void Email_invalido_e_recusado_sem_mostrar_o_valor()
    {
        var usuario = Valido();
        usuario.Email = "email-secreto-invalido";

        var falhas = Falhas(usuario);

        falhas.Should().ContainSingle().Which.Should().StartWith("A configuração UsuariosIniciais:0:Email é inválida:");
        falhas[0].Should().NotContain("email-secreto-invalido");
    }
}
