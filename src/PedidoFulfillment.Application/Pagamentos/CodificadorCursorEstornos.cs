using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Cursor opaco da listagem dos estornos pendentes (REQ-28, R-08), no <see cref="EnvelopeCursor"/>,
/// com o rótulo <c>estornos</c>: ligado a quem chama; a listagem não tem filtros, e o tamanho da página
/// fica de fora.
/// </summary>
public sealed class CodificadorCursorEstornos(IChaveCursor chave)
{
    private const int VersaoCarga = 1;
    private const string SentidoProxima = "p";
    private const string SentidoAnterior = "a";
    private const string SemFiltros = "";

    private static readonly byte[] Rotulo = "estornos"u8.ToArray();

    public string Codificar(PosicaoCursorEstornos posicao, Guid usuarioId)
    {
        var carga = JsonSerializer.SerializeToUtf8Bytes(new CargaCursor
        {
            Versao = VersaoCarga,
            Sentido = posicao.Sentido == SentidoCursor.Proxima ? SentidoProxima : SentidoAnterior,
            SolicitadoEm = posicao.SolicitadoEm,
            Id = posicao.Id,
            Inclusiva = posicao.Inclusiva
        });

        return EnvelopeCursor.Selar(carga, Rotulo, chave.Chave, usuarioId, SemFiltros);
    }

    /// <summary>Nunca lança por entrada ruim: qualquer defeito devolve <see langword="false"/>.</summary>
    public bool TentarDecodificar(string? texto, Guid usuarioId, [NotNullWhen(true)] out PosicaoCursorEstornos? posicao)
    {
        posicao = null;
        if (!EnvelopeCursor.TentarAbrir(texto, Rotulo, chave.Chave, usuarioId, SemFiltros, out var carga))
        {
            return false;
        }

        posicao = Ler(carga);
        return posicao is not null;
    }

    private static PosicaoCursorEstornos? Ler(byte[] carga)
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

        return lida is { Versao: VersaoCarga, SolicitadoEm: { } solicitadoEm, Id: { } id } && sentido is { } s
            ? new PosicaoCursorEstornos(s, solicitadoEm, id, lida.Inclusiva)
            : null;
    }

    private sealed record CargaCursor
    {
        [JsonPropertyName("v")]
        public int Versao { get; init; }

        [JsonPropertyName("s")]
        public string? Sentido { get; init; }

        [JsonPropertyName("t")]
        public DateTimeOffset? SolicitadoEm { get; init; }

        [JsonPropertyName("k")]
        public Guid? Id { get; init; }

        [JsonPropertyName("i")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Inclusiva { get; init; }
    }
}
