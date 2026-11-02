using System.Buffers.Binary;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Notificacoes;

/// <summary>Um aviso lido por cursor, com a <see cref="Posicao"/> como número.</summary>
internal sealed record AvisoLido(
    long Id,
    ulong Posicao,
    Guid IdNotificacao,
    string Tipo,
    Guid PedidoId,
    Guid ClienteId,
    string Conteudo,
    DateTimeOffset CriadoEm,
    string? ContextoRastreio);

/// <summary>
/// Leitura dos avisos por cursor de <c>rowversion</c> (REQ-18, research R-13). Só devolve o que está
/// abaixo de <c>MIN_ACTIVE_ROWVERSION()</c>: um aviso de transação ainda aberta segura os de
/// posição maior, e nenhum aviso confirmado depois aparece atrás do cursor. Usada pelo SSE da API
/// e pela distribuição dos webhooks no Worker.
/// <para>
/// <c>READPAST</c> evita que a leitura espere pelo lock da linha ainda não confirmada (o banco não
/// usa snapshot): toda linha travada por um <c>INSERT</c> aberto está em
/// <c>MIN_ACTIVE_ROWVERSION()</c> ou acima, fora do intervalo lido.
/// </para>
/// </summary>
internal sealed class LeituraAvisos(ContextoBancoDados contexto)
{
    public Task<IReadOnlyList<AvisoLido>> LerAsync(ulong cursor, int lote, CancellationToken cancellationToken) =>
        LerAsync(contexto.Database.SqlQuery<LinhaAviso>($"""
            SELECT TOP ({lote}) Id, Posicao, IdNotificacao, Tipo, PedidoId, ClienteId, Conteudo, CriadoEm, ContextoRastreio
            FROM AvisosPedido WITH (READPAST)
            WHERE Posicao > {Binario(cursor)} AND Posicao < MIN_ACTIVE_ROWVERSION()
            ORDER BY Posicao
            """), cancellationToken);

    public Task<IReadOnlyList<AvisoLido>> LerDoClienteAsync(Guid clienteId, ulong cursor, int lote, CancellationToken cancellationToken) =>
        LerAsync(contexto.Database.SqlQuery<LinhaAviso>($"""
            SELECT TOP ({lote}) Id, Posicao, IdNotificacao, Tipo, PedidoId, ClienteId, Conteudo, CriadoEm, ContextoRastreio
            FROM AvisosPedido WITH (READPAST)
            WHERE ClienteId = {clienteId} AND Posicao > {Binario(cursor)} AND Posicao < MIN_ACTIVE_ROWVERSION()
            ORDER BY Posicao
            """), cancellationToken);

    public Task<IReadOnlyList<AvisoLido>> LerDoPedidoAsync(Guid pedidoId, ulong cursor, int lote, CancellationToken cancellationToken) =>
        LerAsync(contexto.Database.SqlQuery<LinhaAviso>($"""
            SELECT TOP ({lote}) Id, Posicao, IdNotificacao, Tipo, PedidoId, ClienteId, Conteudo, CriadoEm, ContextoRastreio
            FROM AvisosPedido WITH (READPAST)
            WHERE PedidoId = {pedidoId} AND Posicao > {Binario(cursor)} AND Posicao < MIN_ACTIVE_ROWVERSION()
            ORDER BY Posicao
            """), cancellationToken);

    /// <summary>A posição do aviso, ou nulo se ele não existe (nunca existiu ou já foi limpo).</summary>
    public async Task<ulong?> PosicaoDeAsync(Guid idNotificacao, CancellationToken cancellationToken)
    {
        var posicoes = await contexto.Database.SqlQuery<byte[]>($"""
            SELECT Posicao AS Value FROM AvisosPedido WHERE IdNotificacao = {idNotificacao}
            """).ToListAsync(cancellationToken);
        return posicoes.Count == 0 ? null : Numero(posicoes[0]);
    }

    /// <summary>O último aviso confirmado (abaixo de <c>MIN_ACTIVE_ROWVERSION()</c>), ou nulo com a tabela vazia.</summary>
    public async Task<(ulong Posicao, Guid IdNotificacao)?> UltimaConfirmadaAsync(CancellationToken cancellationToken)
    {
        var linhas = await contexto.Database.SqlQuery<LinhaPosicao>($"""
            SELECT TOP (1) Posicao, IdNotificacao FROM AvisosPedido WITH (READPAST)
            WHERE Posicao < MIN_ACTIVE_ROWVERSION()
            ORDER BY Posicao DESC
            """).ToListAsync(cancellationToken);
        return linhas.Count == 0 ? null : (Numero(linhas[0].Posicao), linhas[0].IdNotificacao);
    }

    /// <summary>A posição abaixo da qual tudo já está confirmado, para iniciar um cursor sem reler o passado.</summary>
    public async Task<ulong> PosicaoAtualAsync(CancellationToken cancellationToken)
    {
        var minimo = await contexto.Database.SqlQuery<byte[]>($"SELECT MIN_ACTIVE_ROWVERSION() AS Value").ToListAsync(cancellationToken);
        return Numero(minimo[0]) - 1;
    }

    internal static byte[] Binario(ulong posicao)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, posicao);
        return bytes;
    }

    internal static ulong Numero(byte[] binario) => BinaryPrimitives.ReadUInt64BigEndian(binario);

    private static async Task<IReadOnlyList<AvisoLido>> LerAsync(IQueryable<LinhaAviso> consulta, CancellationToken cancellationToken) =>
        (await consulta.ToListAsync(cancellationToken))
            .Select(l => new AvisoLido(l.Id, Numero(l.Posicao), l.IdNotificacao, l.Tipo, l.PedidoId, l.ClienteId, l.Conteudo, l.CriadoEm, l.ContextoRastreio))
            .ToList();

    private sealed class LinhaAviso
    {
        public long Id { get; init; }

        public byte[] Posicao { get; init; } = [];

        public Guid IdNotificacao { get; init; }

        public string Tipo { get; init; } = "";

        public Guid PedidoId { get; init; }

        public Guid ClienteId { get; init; }

        public string Conteudo { get; init; } = "";

        public DateTimeOffset CriadoEm { get; init; }

        public string? ContextoRastreio { get; init; }
    }

    private sealed class LinhaPosicao
    {
        public byte[] Posicao { get; init; } = [];

        public Guid IdNotificacao { get; init; }
    }
}
