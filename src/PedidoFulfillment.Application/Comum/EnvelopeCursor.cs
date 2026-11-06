using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace PedidoFulfillment.Application.Comum;

/// <summary>
/// A parte comum dos cursores opacos (REQ-22, R-05; REQ-25, R-07): <c>base64url(carga) + "." +
/// base64url(mac)</c>. O <c>mac</c> começa pelo rótulo da listagem, então um cursor de uma nunca vale
/// na outra, e liga o cursor ao usuário e aos filtros. O formato da carga é de cada listagem.
/// </summary>
public static class EnvelopeCursor
{
    private const int TamanhoMac = 16;
    private const int TamanhoMaximo = 1024;

    public static string Selar(byte[] carga, byte[] rotulo, byte[] chave, Guid usuarioId, string filtros) =>
        $"{Base64Url.EncodeToString(carga)}.{Base64Url.EncodeToString(Mac(carga, rotulo, chave, usuarioId, filtros))}";

    /// <summary>Nunca lança por entrada ruim: qualquer defeito devolve <see langword="false"/>.</summary>
    public static bool TentarAbrir(string? texto, byte[] rotulo, byte[] chave, Guid usuarioId, string filtros, out byte[] carga)
    {
        carga = [];
        if (string.IsNullOrEmpty(texto) || texto.Length > TamanhoMaximo)
        {
            return false;
        }

        var partes = texto.Split('.');
        if (partes.Length != 2 || !TentarBase64Url(partes[0], out var lida) || !TentarBase64Url(partes[1], out var mac)
            || mac.Length != TamanhoMac || !CryptographicOperations.FixedTimeEquals(mac, Mac(lida, rotulo, chave, usuarioId, filtros)))
        {
            return false;
        }

        carga = lida;
        return true;
    }

    private static byte[] Mac(byte[] carga, byte[] rotulo, byte[] chave, Guid usuarioId, string filtros)
    {
        // Tamanho da carga na frente: sem ele, bytes poderiam migrar entre a carga e os filtros.
        var textoFiltros = Encoding.UTF8.GetBytes(filtros);
        var dados = new byte[rotulo.Length + sizeof(int) + carga.Length + 16 + textoFiltros.Length];
        var posicao = 0;
        rotulo.CopyTo(dados, posicao);
        posicao += rotulo.Length;
        BinaryPrimitives.WriteInt32BigEndian(dados.AsSpan(posicao), carga.Length);
        posicao += sizeof(int);
        carga.CopyTo(dados, posicao);
        posicao += carga.Length;
        usuarioId.TryWriteBytes(dados.AsSpan(posicao, 16));
        posicao += 16;
        textoFiltros.CopyTo(dados, posicao);
        return HMACSHA256.HashData(chave, dados)[..TamanhoMac];
    }

    private static bool TentarBase64Url(string texto, out byte[] bytes)
    {
        try
        {
            bytes = Base64Url.DecodeFromChars(texto);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
