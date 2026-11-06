using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Usuarios;
using PedidoFulfillment.Application.Usuarios;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Testes.Unitarios.Usuarios;

/// <summary>Parâmetros e filtros da listagem de usuários (REQ-25, R-07).</summary>
public sealed class FiltroListagemUsuariosTestes
{
    private static List<ValidationResult> Validar(ListarUsuariosRequestDTO requisicao) =>
        [.. requisicao.Validate(new ValidationContext(requisicao))];

    [Fact]
    public void Sem_parametros_e_valido_com_o_tamanho_padrao()
    {
        var requisicao = new ListarUsuariosRequestDTO();

        Validar(requisicao).Should().BeEmpty();
        requisicao.TamanhoPagina().Should().Be(ListarUsuariosRequestDTO.TamanhoPadrao);
        requisicao.PerfilPedido().Should().BeNull();
        requisicao.SituacaoPedida().Should().BeNull();
        requisicao.BuscaAparada().Should().BeNull();
    }

    [Theory]
    [InlineData("Admin", PerfilUsuario.Admin)]
    [InlineData("OperadorLogistica", PerfilUsuario.OperadorLogistica)]
    [InlineData("Cliente", PerfilUsuario.Cliente)]
    public void Perfil_conhecido_e_aceito(string texto, PerfilUsuario esperado)
    {
        var requisicao = new ListarUsuariosRequestDTO { Perfil = texto };

        Validar(requisicao).Should().BeEmpty();
        requisicao.PerfilPedido().Should().Be(esperado);
    }

    [Theory]
    [InlineData("Auditor")]
    [InlineData("admin")]
    [InlineData("1")]
    [InlineData("")]
    public void Perfil_desconhecido_e_recusado(string texto) =>
        Validar(new ListarUsuariosRequestDTO { Perfil = texto }).Should().ContainSingle()
            .Which.MemberNames.Should().Equal("perfil");

    [Theory]
    [InlineData("Ativa", SituacaoUsuario.Ativa)]
    [InlineData("Desativada", SituacaoUsuario.Desativada)]
    public void Situacao_conhecida_e_aceita(string texto, SituacaoUsuario esperada)
    {
        var requisicao = new ListarUsuariosRequestDTO { Situacao = texto };

        Validar(requisicao).Should().BeEmpty();
        requisicao.SituacaoPedida().Should().Be(esperada);
    }

    [Theory]
    [InlineData("Ativo")]
    [InlineData("0")]
    public void Situacao_desconhecida_e_recusada(string texto) =>
        Validar(new ListarUsuariosRequestDTO { Situacao = texto }).Should().ContainSingle()
            .Which.MemberNames.Should().Equal("situacao");

    [Theory]
    [InlineData("a")]
    [InlineData("  a  ")]
    [InlineData("   ")]
    public void Busca_curta_demais_e_recusada(string texto) =>
        Validar(new ListarUsuariosRequestDTO { Busca = texto }).Should().ContainSingle()
            .Which.MemberNames.Should().Equal("busca");

    [Fact]
    public void Busca_longa_demais_e_recusada() =>
        Validar(new ListarUsuariosRequestDTO { Busca = new string('a', ListarUsuariosRequestDTO.TamanhoMaximoBusca + 1) }).Should().ContainSingle();

    [Fact]
    public void Busca_e_aparada_e_os_espacos_nao_contam_no_tamanho()
    {
        var requisicao = new ListarUsuariosRequestDTO { Busca = $"  {new string('a', ListarUsuariosRequestDTO.TamanhoMaximoBusca)}  " };

        Validar(requisicao).Should().BeEmpty();
        requisicao.BuscaAparada().Should().HaveLength(ListarUsuariosRequestDTO.TamanhoMaximoBusca);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("-1")]
    [InlineData("abc")]
    public void Tamanho_fora_da_faixa_e_recusado(string texto) =>
        Validar(new ListarUsuariosRequestDTO { Tamanho = texto }).Should().ContainSingle()
            .Which.MemberNames.Should().Equal("tamanho");

    [Theory]
    [InlineData("1", 1)]
    [InlineData("100", 100)]
    public void Tamanho_na_faixa_e_aceito(string texto, int esperado)
    {
        var requisicao = new ListarUsuariosRequestDTO { Tamanho = texto };

        Validar(requisicao).Should().BeEmpty();
        requisicao.TamanhoPagina().Should().Be(esperado);
    }

    [Fact]
    public void Traz_todos_os_erros_de_uma_vez() =>
        Validar(new ListarUsuariosRequestDTO { Perfil = "x", Situacao = "x", Busca = "x", Tamanho = "x" }).Should().HaveCount(4);

    [Fact]
    public void Texto_canonico_nao_diferencia_maiusculas_da_busca_e_muda_com_os_filtros()
    {
        var filtro = new FiltroListagemUsuarios { Perfil = PerfilUsuario.Admin, Situacao = SituacaoUsuario.Ativa, Busca = "Lima" };

        filtro.TextoCanonico().Should().Be((filtro with { Busca = "LIMA" }).TextoCanonico());
        filtro.TextoCanonico().Should().NotBe((filtro with { Perfil = null }).TextoCanonico());
        filtro.TextoCanonico().Should().NotBe((filtro with { Situacao = null }).TextoCanonico());
        filtro.TextoCanonico().Should().NotBe((filtro with { Busca = null }).TextoCanonico());
    }
}
