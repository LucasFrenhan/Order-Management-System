using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.Application.Usuarios;

/// <summary>
/// Cursor opaco da listagem de usuários (REQ-25, R-07), no <see cref="EnvelopeCursor"/>, com o rótulo
/// <c>usuarios</c>: ligado a quem chama e aos filtros; o tamanho da página fica de fora.
/// </summary>
public sealed class CodificadorCursorUsuarios(IChaveCursor chave)
{
    private const int VersaoCarga = 1;
    private const string SentidoProxima = "p";
    private const string SentidoAnterior = "a";

    private static readonly byte[] Rotulo = "usuarios"u8.ToArray();

    public string Codificar(PosicaoCursorUsuarios posicao, Guid usuarioId, FiltroListagemUsuarios filtro)
    {
        var carga = JsonSerializer.SerializeToUtf8Bytes(new CargaCursor
        {
            Versao = VersaoCarga,
            Sentido = posicao.Sentido == SentidoCursor.Proxima ? SentidoProxima : SentidoAnterior,
            Nome = posicao.Nome,
            Id = posicao.Id,
            Inclusiva = posicao.Inclusiva
        });

        return EnvelopeCursor.Selar(carga, Rotulo, chave.Chave, usuarioId, filtro.TextoCanonico());
    }

    /// <summary>Nunca lança por entrada ruim: qualquer defeito devolve <see langword="false"/>.</summary>
    public bool TentarDecodificar(string? texto, Guid usuarioId, FiltroListagemUsuarios filtro, [NotNullWhen(true)] out PosicaoCursorUsuarios? posicao)
    {
        posicao = null;
        if (!EnvelopeCursor.TentarAbrir(texto, Rotulo, chave.Chave, usuarioId, filtro.TextoCanonico(), out var carga))
        {
            return false;
        }

        posicao = Ler(carga);
        return posicao is not null;
    }

    private static PosicaoCursorUsuarios? Ler(byte[] carga)
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

        return lida is { Versao: VersaoCarga, Nome: { Length: > 0 } nome, Id: { } id } && sentido is { } s
            ? new PosicaoCursorUsuarios(s, nome, id, lida.Inclusiva)
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
        public Guid? Id { get; init; }

        [JsonPropertyName("i")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public bool Inclusiva { get; init; }
    }
}
