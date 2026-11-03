using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PedidoFulfillment.Application.Pedidos;

/// <summary>
/// Chave do HMAC do cursor da listagem (REQ-21, research R-04). A implementação de hoje deriva a
/// chave da chave de assinatura do JWT; ela pode passar a uma chave própria do cursor trocando só
/// o registro desta interface na API. A troca invalida os cursores em uso (400).
/// </summary>
public interface IChaveCursorPedidos
{
    /// <summary>Bytes da chave; ao menos 32.</summary>
    byte[] Chave { get; }
}

/// <summary>
/// Cursor opaco da listagem (research R-03): <c>base64url(carga) + "." + base64url(mac)</c>. O
/// <c>mac</c> liga o cursor ao usuário e aos filtros; o tamanho da página fica de fora. O escopo
/// do Cliente não depende do cursor: ele é aplicado no servidor (FR-004).
/// </summary>
public sealed class CodificadorCursorPedidos(IChaveCursorPedidos chave)
{
    private const int VersaoCarga = 1;
    private const int TamanhoMac = 16;

    /// <summary>Acima disso o texto não é um cursor nosso; evita decodificar entradas enormes.</summary>
    private const int TamanhoMaximo = 512;

    private const string SentidoProxima = "p";
    private const string SentidoAnterior = "a";

    public string Codificar(PosicaoCursor posicao, Guid usuarioId, FiltroListagemPedidos filtro)
    {
        var carga = JsonSerializer.SerializeToUtf8Bytes(new CargaCursor
        {
            Versao = VersaoCarga,
            Sentido = posicao.Sentido == SentidoCursor.Proxima ? SentidoProxima : SentidoAnterior,
            CriadoEm = posicao.CriadoEm.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            Numero = posicao.Numero,
            Inclusiva = posicao.Inclusiva
        });

        return $"{Base64Url.EncodeToString(carga)}.{Base64Url.EncodeToString(Mac(carga, usuarioId, filtro))}";
    }

    /// <summary>Nunca lança por entrada ruim: qualquer defeito devolve <see langword="false"/>.</summary>
    public bool TentarDecodificar(string? texto, Guid usuarioId, FiltroListagemPedidos filtro, [NotNullWhen(true)] out PosicaoCursor? posicao)
    {
        posicao = null;
        if (string.IsNullOrEmpty(texto) || texto.Length > TamanhoMaximo)
        {
            return false;
        }

        var partes = texto.Split('.');
        if (partes.Length != 2 || !TentarBase64Url(partes[0], out var carga) || !TentarBase64Url(partes[1], out var mac)
            || mac.Length != TamanhoMac || !CryptographicOperations.FixedTimeEquals(mac, Mac(carga, usuarioId, filtro)))
        {
            return false;
        }

        posicao = Ler(carga);
        return posicao is not null;
    }

    private byte[] Mac(byte[] carga, Guid usuarioId, FiltroListagemPedidos filtro)
    {
        // Tamanho da carga na frente: sem ele, bytes poderiam migrar entre a carga e os filtros.
        var filtros = Encoding.UTF8.GetBytes(filtro.TextoCanonico());
        var dados = new byte[sizeof(int) + carga.Length + 16 + filtros.Length];
        BinaryPrimitives.WriteInt32BigEndian(dados, carga.Length);
        carga.CopyTo(dados, sizeof(int));
        usuarioId.TryWriteBytes(dados.AsSpan(sizeof(int) + carga.Length, 16));
        filtros.CopyTo(dados, sizeof(int) + carga.Length + 16);
        return HMACSHA256.HashData(chave.Chave, dados)[..TamanhoMac];
    }

    private static PosicaoCursor? Ler(byte[] carga)
    {
        CargaCursor? lida;
        try
        {
            lida = JsonSerializer.Deserialize<CargaCursor>(carga);
        }
        catch (JsonException)
        {
            return null;
        }

        var sentido = lida?.Sentido switch
        {
            SentidoProxima => SentidoCursor.Proxima,
            SentidoAnterior => SentidoCursor.Anterior,
            _ => (SentidoCursor?)null
        };

        return lida is { Versao: VersaoCarga, Numero: { Length: > 0 } numero } && sentido is { } s
            && DateTimeOffset.TryParseExact(lida.CriadoEm, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var criadoEm)
            ? new PosicaoCursor(s, criadoEm, numero, lida.Inclusiva)
            : null;
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

    private sealed record CargaCursor
    {
        [JsonPropertyName("v")]
        public int Versao { get; init; }

        [JsonPropertyName("s")]
        public string? Sentido { get; init; }

        [JsonPropertyName("c")]
        public string? CriadoEm { get; init; }

        [JsonPropertyName("n")]
        public string? Numero { get; init; }

        [JsonPropertyName("i")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Inclusiva { get; init; }
    }
}
