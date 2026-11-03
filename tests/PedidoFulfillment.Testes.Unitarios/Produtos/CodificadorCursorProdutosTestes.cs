using System.Buffers.Text;
using System.Text;
using FluentAssertions;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Application.Produtos;
using PedidoFulfillment.Testes.Unitarios.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Produtos;

/// <summary>Cursor opaco da listagem de produtos (REQ-22, research R-05).</summary>
public sealed class CodificadorCursorProdutosTestes
{
    private static readonly Guid Usuario = Guid.Parse("0199f000-0000-7000-8000-000000000001");
    private static readonly Guid Outro = Guid.Parse("0199f000-0000-7000-8000-000000000002");
    private static readonly CodificadorCursorPedidosTestes.ChaveFixa Chave = new("chave-de-teste-com-mais-de-32-bytes!");

    private static readonly FiltroListagemProdutos Filtro = new()
    {
        Palavras = FiltroListagemProdutos.ExtrairPalavras("café torrado"),
        Situacao = SituacaoProduto.Ativos
    };

    private static readonly PosicaoCursorProdutos Posicao = new(SentidoCursor.Proxima, "Café torrado 250 g", "CAF-250");

    private static CodificadorCursorProdutos Codificador() => new(Chave);

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
    public void Carga_ou_mac_alterados_sao_recusados()
    {
        var cursor = Codificador().Codificar(Posicao, Usuario, Filtro);
        var partes = cursor.Split('.');
        var carga = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(partes[0])).Replace("CAF-250", "CAF-999");

        Codificador().TentarDecodificar($"{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(carga))}.{partes[1]}", Usuario, Filtro, out _).Should().BeFalse();
        Codificador().TentarDecodificar(cursor[..^1] + (cursor[^1] == 'A' ? 'B' : 'A'), Usuario, Filtro, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("semponto")]
    [InlineData("a.b.c")]
    [InlineData("!!!.???")]
    public void Texto_malformado_e_recusado_sem_excecao(string texto)
    {
        Codificador().TentarDecodificar(texto, Usuario, Filtro, out _).Should().BeFalse();
    }

    [Fact]
    public void Outro_usuario_ou_outros_filtros_sao_recusados()
    {
        var cursor = Codificador().Codificar(Posicao, Usuario, Filtro);

        Codificador().TentarDecodificar(cursor, Outro, Filtro, out _).Should().BeFalse();
        Codificador().TentarDecodificar(cursor, Usuario, Filtro with { Situacao = SituacaoProduto.Todos }, out _).Should().BeFalse();
        Codificador().TentarDecodificar(cursor, Usuario, Filtro with { Palavras = ["cafe"] }, out _).Should().BeFalse();
    }

    [Fact]
    public void Mesma_busca_com_outra_caixa_e_ordem_vale()
    {
        var cursor = Codificador().Codificar(Posicao, Usuario, Filtro);
        var equivalente = Filtro with { Palavras = FiltroListagemProdutos.ExtrairPalavras("TORRADO Café") };

        Codificador().TentarDecodificar(cursor, Usuario, equivalente, out _).Should().BeTrue();
    }

    [Fact]
    public void Cursor_de_produtos_nao_vale_na_listagem_de_pedidos_e_vice_versa()
    {
        var deProdutos = Codificador().Codificar(Posicao, Usuario, Filtro);
        var dePedidos = new CodificadorCursorPedidos(Chave).Codificar(
            new PosicaoCursor(SentidoCursor.Proxima, DateTimeOffset.UtcNow, "PED-20261103-000001"), Usuario, new FiltroListagemPedidos());

        new CodificadorCursorPedidos(Chave).TentarDecodificar(deProdutos, Usuario, new FiltroListagemPedidos(), out _).Should().BeFalse();
        Codificador().TentarDecodificar(dePedidos, Usuario, Filtro, out _).Should().BeFalse();
    }

    [Fact]
    public void Cursor_nao_mostra_o_usuario()
    {
        Codificador().Codificar(Posicao, Usuario, Filtro).Should().NotContain(Usuario.ToString());
    }
}
