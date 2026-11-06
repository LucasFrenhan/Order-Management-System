using System.Buffers.Text;
using System.Text;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Usuarios;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Application.Produtos;
using PedidoFulfillment.Application.Usuarios;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Unitarios.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Usuarios;

/// <summary>Cursor opaco da listagem de usuários (REQ-25, R-07).</summary>
public sealed class CodificadorCursorUsuariosTestes
{
    private static readonly Guid Usuario = Guid.Parse("0199f000-0000-7000-8000-000000000001");
    private static readonly Guid Outro = Guid.Parse("0199f000-0000-7000-8000-000000000002");
    private static readonly Guid Conta = Guid.Parse("0199f000-0000-7000-8000-000000000003");
    private static readonly CodificadorCursorPedidosTestes.ChaveFixa Chave = new("chave-de-teste-com-mais-de-32-bytes!");

    private static readonly FiltroListagemUsuarios Filtro = new()
    {
        Perfil = PerfilUsuario.Admin,
        Situacao = SituacaoUsuario.Ativa,
        Busca = "lima"
    };

    private static readonly PosicaoCursorUsuarios Posicao = new(SentidoCursor.Proxima, "Ana Lima", Conta);

    private static CodificadorCursorUsuarios Codificador() => new(Chave);

    [Theory]
    [InlineData(SentidoCursor.Proxima, false)]
    [InlineData(SentidoCursor.Anterior, false)]
    [InlineData(SentidoCursor.Anterior, true)]
    public void Ida_e_volta_preserva_a_posicao(SentidoCursor sentido, bool inclusiva)
    {
        var posicao = Posicao with { Sentido = sentido, Inclusiva = inclusiva };

        Codificador().TentarDecodificar(Codificador().Codificar(posicao, Usuario, Filtro), Usuario, Filtro, out var lida).Should().BeTrue();

        lida.Should().Be(posicao);
    }

    [Fact]
    public void Carga_alterada_e_recusada()
    {
        var partes = Codificador().Codificar(Posicao, Usuario, Filtro).Split('.');
        var carga = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(partes[0])).Replace("Ana Lima", "Bia Lima");

        Codificador().TentarDecodificar($"{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(carga))}.{partes[1]}", Usuario, Filtro, out _).Should().BeFalse();
    }

    [Fact]
    public void Cursor_de_outro_usuario_e_recusado() =>
        Codificador().TentarDecodificar(Codificador().Codificar(Posicao, Usuario, Filtro), Outro, Filtro, out _).Should().BeFalse();

    public static TheoryData<FiltroListagemUsuarios> OutrosFiltros => new()
    {
        Filtro with { Perfil = PerfilUsuario.Cliente },
        Filtro with { Situacao = SituacaoUsuario.Desativada },
        Filtro with { Busca = "souza" },
    };

    [Theory]
    [MemberData(nameof(OutrosFiltros))]
    public void Cursor_de_outros_filtros_e_recusado(FiltroListagemUsuarios outro) =>
        Codificador().TentarDecodificar(Codificador().Codificar(Posicao, Usuario, Filtro), Usuario, outro, out _).Should().BeFalse();

    [Fact]
    public void Cursor_de_produtos_nao_vale_em_usuarios()
    {
        var filtro = new FiltroListagemProdutos();
        var deProdutos = new CodificadorCursorProdutos(Chave).Codificar(new PosicaoCursorProdutos(SentidoCursor.Proxima, "Ana", "SKU"), Usuario, filtro);

        Codificador().TentarDecodificar(deProdutos, Usuario, new FiltroListagemUsuarios(), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sem-ponto")]
    [InlineData("a.b.c")]
    [InlineData("!!!.???")]
    public void Texto_ruim_e_recusado_sem_lancar(string? texto) =>
        Codificador().TentarDecodificar(texto, Usuario, Filtro, out _).Should().BeFalse();

    [Fact]
    public void Texto_longo_demais_e_recusado() =>
        Codificador().TentarDecodificar(new string('a', 1025), Usuario, Filtro, out _).Should().BeFalse();
}
