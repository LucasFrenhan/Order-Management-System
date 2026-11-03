using System.Buffers.Text;
using System.Text;
using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Pedidos;

/// <summary>Cursor opaco da listagem (REQ-21, research R-03, FR-017).</summary>
public sealed class CodificadorCursorPedidosTestes
{
    private static readonly Guid Usuario = Guid.Parse("0199f000-0000-7000-8000-000000000001");
    private static readonly Guid Outro = Guid.Parse("0199f000-0000-7000-8000-000000000002");
    private static readonly DateTimeOffset Instante = new(2026, 11, 3, 13, 10, 0, 123, TimeSpan.Zero);

    private static readonly FiltroListagemPedidos Filtro = new()
    {
        Status = new HashSet<StatusPedido> { StatusPedido.Pendente, StatusPedido.Processando },
        CriadoDe = Instante.AddDays(-7)
    };

    private static readonly PosicaoCursor Posicao = new(SentidoCursor.Proxima, Instante, "PED-20261103-000002");

    private static CodificadorCursorPedidos Codificador(string chave = "chave-de-teste-com-mais-de-32-bytes!") => new(new ChaveFixa(chave));

    [Theory]
    [InlineData(SentidoCursor.Proxima, false)]
    [InlineData(SentidoCursor.Anterior, false)]
    [InlineData(SentidoCursor.Anterior, true)]
    public void Ida_e_volta_preserva_a_posicao(SentidoCursor sentido, bool inclusiva)
    {
        var posicao = Posicao with { Sentido = sentido, Inclusiva = inclusiva, CriadoEm = Instante.ToOffset(TimeSpan.FromHours(-3)) };
        var cursor = Codificador().Codificar(posicao, Usuario, Filtro);

        Codificador().TentarDecodificar(cursor, Usuario, Filtro, out var lida).Should().BeTrue();

        lida.Should().Be(posicao);
        lida!.CriadoEm.UtcDateTime.Should().Be(Instante.UtcDateTime);
    }

    [Fact]
    public void Cursor_nao_mostra_usuario_nem_cliente()
    {
        var filtro = Filtro with { ClienteId = Outro };
        var cursor = Codificador().Codificar(Posicao, Usuario, filtro);
        var carga = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor.Split('.')[0]));

        foreach (var texto in new[] { cursor, carga })
        {
            texto.Should().NotContain(Usuario.ToString()).And.NotContain(Outro.ToString());
        }
    }

    [Fact]
    public void Carga_alterada_e_recusada()
    {
        var cursor = Codificador().Codificar(Posicao, Usuario, Filtro);
        var partes = cursor.Split('.');
        var carga = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(partes[0])).Replace("000002", "000009");
        var adulterado = $"{Base64Url.EncodeToString(Encoding.UTF8.GetBytes(carga))}.{partes[1]}";

        Codificador().TentarDecodificar(adulterado, Usuario, Filtro, out var posicao).Should().BeFalse();
        posicao.Should().BeNull();
    }

    [Fact]
    public void Mac_alterado_e_recusado()
    {
        var cursor = Codificador().Codificar(Posicao, Usuario, Filtro);
        var ultimo = cursor[^1] == 'A' ? 'B' : 'A';

        Codificador().TentarDecodificar(cursor[..^1] + ultimo, Usuario, Filtro, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("semponto")]
    [InlineData("a.b.c")]
    [InlineData("!!!.???")]
    [InlineData(".")]
    public void Texto_malformado_e_recusado_sem_excecao(string texto)
    {
        Codificador().TentarDecodificar(texto, Usuario, Filtro, out _).Should().BeFalse();
    }

    [Fact]
    public void Texto_enorme_e_recusado()
    {
        Codificador().TentarDecodificar(new string('a', 600) + ".b", Usuario, Filtro, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"v\":2,\"s\":\"p\",\"c\":\"2026-11-03T13:10:00.0000000+00:00\",\"n\":\"PED-20261103-000002\"}")]
    [InlineData("{\"v\":1,\"s\":\"x\",\"c\":\"2026-11-03T13:10:00.0000000+00:00\",\"n\":\"PED-20261103-000002\"}")]
    [InlineData("{\"v\":1,\"s\":\"p\",\"c\":\"ontem\",\"n\":\"PED-20261103-000002\"}")]
    [InlineData("{\"v\":1,\"s\":\"p\",\"c\":\"2026-11-03T13:10:00.0000000+00:00\"}")]
    [InlineData("não é json")]
    public void Carga_com_mac_valido_mas_conteudo_invalido_e_recusada(string carga)
    {
        var cursor = CursorComMacValido(carga, Usuario, Filtro);

        Codificador().TentarDecodificar(cursor, Usuario, Filtro, out _).Should().BeFalse();
    }

    [Fact]
    public void Cursor_de_outro_usuario_e_recusado()
    {
        var cursor = Codificador().Codificar(Posicao, Outro, Filtro);

        Codificador().TentarDecodificar(cursor, Usuario, Filtro, out _).Should().BeFalse();
    }

    public static TheoryData<FiltroListagemPedidos> OutrosFiltros() =>
    [
        Filtro with { Status = new HashSet<StatusPedido> { StatusPedido.Pendente, StatusPedido.Processando, StatusPedido.Cancelado } },
        Filtro with { CriadoDe = Instante.AddDays(-6) },
        Filtro with { CriadoAte = Instante },
        Filtro with { AtualizadoDe = Instante },
        Filtro with { AtualizadoAte = Instante },
        Filtro with { ClienteId = Outro }
    ];

    [Theory]
    [MemberData(nameof(OutrosFiltros))]
    public void Cursor_usado_com_outros_filtros_e_recusado(FiltroListagemPedidos outros)
    {
        var cursor = Codificador().Codificar(Posicao, Usuario, Filtro);

        Codificador().TentarDecodificar(cursor, Usuario, outros, out _).Should().BeFalse();
    }

    [Fact]
    public void Mesmos_status_em_outra_ordem_e_mesma_data_em_outro_fuso_sao_os_mesmos_filtros()
    {
        var cursor = Codificador().Codificar(Posicao, Usuario, Filtro);
        var equivalente = Filtro with
        {
            Status = new HashSet<StatusPedido> { StatusPedido.Processando, StatusPedido.Pendente },
            CriadoDe = Filtro.CriadoDe!.Value.ToOffset(TimeSpan.FromHours(-3))
        };

        Codificador().TentarDecodificar(cursor, Usuario, equivalente, out _).Should().BeTrue();
    }

    [Fact]
    public void Chave_diferente_recusa_o_cursor()
    {
        var cursor = Codificador().Codificar(Posicao, Usuario, Filtro);

        Codificador("outra-chave-de-teste-com-mais-de-32-bytes").TentarDecodificar(cursor, Usuario, Filtro, out _).Should().BeFalse();
    }

    /// <summary>Monta um cursor com a carga dada e o <c>mac</c> certo, tirando o mac de um cursor real.</summary>
    private static string CursorComMacValido(string carga, Guid usuario, FiltroListagemPedidos filtro)
    {
        var bytes = Encoding.UTF8.GetBytes(carga);
        var dados = new byte[4 + bytes.Length + 16 + Encoding.UTF8.GetByteCount(filtro.TextoCanonico())];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(dados, bytes.Length);
        bytes.CopyTo(dados, 4);
        usuario.TryWriteBytes(dados.AsSpan(4 + bytes.Length, 16));
        Encoding.UTF8.GetBytes(filtro.TextoCanonico()).CopyTo(dados, 4 + bytes.Length + 16);
        var mac = System.Security.Cryptography.HMACSHA256.HashData(new ChaveFixa("chave-de-teste-com-mais-de-32-bytes!").Chave, dados)[..16];
        return $"{Base64Url.EncodeToString(bytes)}.{Base64Url.EncodeToString(mac)}";
    }

    internal sealed class ChaveFixa(string texto) : IChaveCursor
    {
        public byte[] Chave { get; } = Encoding.UTF8.GetBytes(texto);
    }
}
