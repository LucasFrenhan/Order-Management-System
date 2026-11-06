using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.Application.Produtos;

/// <summary>
/// Cursor opaco da listagem de produtos (REQ-22, research R-05), com a mesma estrutura do de
/// pedidos (REQ-21, R-03), no <see cref="EnvelopeCursor"/>. O rótulo é <c>produtos</c>, e o cursor
/// fica ligado ao usuário e aos filtros (busca e situação); o tamanho da página fica de fora.
/// </summary>
public sealed class CodificadorCursorProdutos(IChaveCursor chave)
{
    private const int VersaoCarga = 1;
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

        return EnvelopeCursor.Selar(carga, Rotulo, chave.Chave, usuarioId, filtro.TextoCanonico());
    }

    /// <summary>Nunca lança por entrada ruim: qualquer defeito devolve <see langword="false"/>.</summary>
    public bool TentarDecodificar(string? texto, Guid usuarioId, FiltroListagemProdutos filtro, [NotNullWhen(true)] out PosicaoCursorProdutos? posicao)
    {
        posicao = null;
        if (!EnvelopeCursor.TentarAbrir(texto, Rotulo, chave.Chave, usuarioId, filtro.TextoCanonico(), out var carga))
        {
            return false;
        }

        posicao = Ler(carga);
        return posicao is not null;
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
