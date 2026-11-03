using System.Buffers.Binary;
using System.Buffers.Text;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.Application.Produtos;

/// <summary>
/// Cursor opaco da listagem de produtos (REQ-22, research R-05), com a mesma estrutura do de
/// pedidos (REQ-21, R-03): <c>base64url(carga) + "." + base64url(mac)</c>. O <c>mac</c> começa
/// pelo rótulo <c>produtos</c>, então um cursor de uma listagem nunca vale na outra, e liga o
/// cursor ao usuário e aos filtros (busca e situação); o tamanho da página fica de fora.
/// </summary>
public sealed class CodificadorCursorProdutos(IChaveCursor chave)
{
    private const int VersaoCarga = 1;
    private const int TamanhoMac = 16;
    private const int TamanhoMaximo = 1024;
    private const string SentidoProxima = "p";
    private const string SentidoAnterior = "a";

    private static readonly byte[] Rotulo = "produtos"u8.ToArray();

    public string Codificar(PosicaoCursorProdutos posicao, Guid usuarioId, FiltroListagemProdutos filtro)
    {
        var carga = JsonSerializer.SerializeToUtf8Bytes(new CargaCursor
        {
            Versao = VersaoCarga,
            Sentido = posicao.Sentido == SentidoCursor.Proxima ? SentidoProxima : SentidoAnterior,
            Nome = posicao.Nome,
            Sku = posicao.Sku,
            Inclusiva = posicao.Inclusiva
        });

        return $"{Base64Url.EncodeToString(carga)}.{Base64Url.EncodeToString(Mac(carga, usuarioId, filtro))}";
    }

    /// <summary>Nunca lança por entrada ruim: qualquer defeito devolve <see langword="false"/>.</summary>
    public bool TentarDecodificar(string? texto, Guid usuarioId, FiltroListagemProdutos filtro, [NotNullWhen(true)] out PosicaoCursorProdutos? posicao)
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

    private byte[] Mac(byte[] carga, Guid usuarioId, FiltroListagemProdutos filtro)
    {
        // Tamanho da carga na frente: sem ele, bytes poderiam migrar entre a carga e os filtros.
        var filtros = Encoding.UTF8.GetBytes(filtro.TextoCanonico());
        var dados = new byte[Rotulo.Length + sizeof(int) + carga.Length + 16 + filtros.Length];
        var posicao = 0;
        Rotulo.CopyTo(dados, posicao);
        posicao += Rotulo.Length;
        BinaryPrimitives.WriteInt32BigEndian(dados.AsSpan(posicao), carga.Length);
        posicao += sizeof(int);
        carga.CopyTo(dados, posicao);
        posicao += carga.Length;
        usuarioId.TryWriteBytes(dados.AsSpan(posicao, 16));
        posicao += 16;
        filtros.CopyTo(dados, posicao);
        return HMACSHA256.HashData(chave.Chave, dados)[..TamanhoMac];
    }

    private static PosicaoCursorProdutos? Ler(byte[] carga)
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

        return lida is { Versao: VersaoCarga, Nome: { Length: > 0 } nome, Sku: { Length: > 0 } sku } && sentido is { } s
            ? new PosicaoCursorProdutos(s, nome, sku, lida.Inclusiva)
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

        [JsonPropertyName("n")]
        public string? Nome { get; init; }

        [JsonPropertyName("k")]
        public string? Sku { get; init; }

        [JsonPropertyName("i")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Inclusiva { get; init; }
    }
}
