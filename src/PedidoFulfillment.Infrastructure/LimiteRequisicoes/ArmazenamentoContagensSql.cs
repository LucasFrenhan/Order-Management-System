using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.LimiteRequisicoes;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.LimiteRequisicoes;

/// <summary>
/// O estado único dos limites no SQL Server (REQ-23, R-01 e R-03). Um só lote por requisição: lê as
/// contagens com bloqueio, em ordem de chave, e só incrementa se nenhuma atingiu o limite. O relógio
/// é o do banco (FR-008). Roda em um pool de conexões próprio, para uma inundação não esgotar as
/// conexões dos pedidos (R-04).
/// </summary>
internal sealed class ArmazenamentoContagensSql(IOptions<OpcoesSqlServer> sqlServer) : IArmazenamentoContagens
{
    internal const string NomeAplicacao = "oms-limite-requisicoes";

    private const int MaximoConexoes = 20;
    private const int MinimoConexoes = 2;
    private const int NumeroDeadlock = 1205;

    private const string Lote = """
        SET NOCOUNT ON; SET XACT_ABORT ON; SET DEADLOCK_PRIORITY HIGH;
        DECLARE @agora datetime2(3) = SYSUTCDATETIME();
        DECLARE @c1 int = 0, @f1 datetime2(3), @b1 bit = 0, @c2 int = 0, @f2 datetime2(3), @b2 bit = 0;

        BEGIN TRANSACTION;

        SELECT @c1 = CASE WHEN FimJanela > @agora THEN Contador ELSE 0 END, @f1 = FimJanela,
               @b1 = CASE WHEN FimJanela > @agora THEN BloqueioRegistrado ELSE 0 END
        FROM ContagensLimite WITH (UPDLOCK, HOLDLOCK) WHERE Chave = @k1;

        IF @k2 IS NOT NULL
            SELECT @c2 = CASE WHEN FimJanela > @agora THEN Contador ELSE 0 END, @f2 = FimJanela,
                   @b2 = CASE WHEN FimJanela > @agora THEN BloqueioRegistrado ELSE 0 END
            FROM ContagensLimite WITH (UPDLOCK, HOLDLOCK) WHERE Chave = @k2;

        DECLARE @r1 bit = IIF(@c1 >= @l1, 1, 0), @r2 bit = IIF(@k2 IS NOT NULL AND @c2 >= @l2, 1, 0);

        IF @r1 = 1 OR @r2 = 1
        BEGIN
            -- Recusada: só a primeira recusa da janela escreve, para o log único (FR-020).
            IF @r1 = 1 AND @b1 = 0 UPDATE ContagensLimite SET BloqueioRegistrado = 1 WHERE Chave = @k1;
            IF @r2 = 1 AND @b2 = 0 UPDATE ContagensLimite SET BloqueioRegistrado = 1 WHERE Chave = @k2;
        END
        ELSE
        BEGIN
            IF @f1 IS NULL
                INSERT ContagensLimite (Chave, Contador, FimJanela, BloqueioRegistrado) VALUES (@k1, 1, DATEADD(SECOND, @j1, @agora), 0);
            ELSE IF @f1 > @agora
                UPDATE ContagensLimite SET Contador = Contador + 1 WHERE Chave = @k1;
            ELSE
                UPDATE ContagensLimite SET Contador = 1, FimJanela = DATEADD(SECOND, @j1, @agora), BloqueioRegistrado = 0 WHERE Chave = @k1;

            IF @k2 IS NOT NULL
            BEGIN
                IF @f2 IS NULL
                    INSERT ContagensLimite (Chave, Contador, FimJanela, BloqueioRegistrado) VALUES (@k2, 1, DATEADD(SECOND, @j2, @agora), 0);
                ELSE IF @f2 > @agora
                    UPDATE ContagensLimite SET Contador = Contador + 1 WHERE Chave = @k2;
                ELSE
                    UPDATE ContagensLimite SET Contador = 1, FimJanela = DATEADD(SECOND, @j2, @agora), BloqueioRegistrado = 0 WHERE Chave = @k2;
            END
        END

        COMMIT TRANSACTION;

        SELECT 1 AS Posicao, DATEDIFF(MILLISECOND, @agora, @f1) AS EsperaMs, CAST(1 - @b1 AS bit) AS PrimeiroBloqueio WHERE @r1 = 1
        UNION ALL
        SELECT 2, DATEDIFF(MILLISECOND, @agora, @f2), CAST(1 - @b2 AS bit) WHERE @r2 = 1;
        """;

    // Preguiçoso: a string de conexão só é lida depois da validação das opções na partida.
    private readonly Lazy<DbContextOptions<ContextoBancoDados>> opcoesContexto = new(() =>
        new DbContextOptionsBuilder<ContextoBancoDados>().UseSqlServer(StringConexao(sqlServer.Value.StringConexao)).Options);

    public async Task<IReadOnlyList<RecusaContagem>> ContarAsync(IReadOnlyList<ContagemSolicitada> contagens, CancellationToken cancellationToken)
    {
        // Sempre na mesma ordem de chave: transações simultâneas nunca cruzam os bloqueios (R-03).
        var ordem = contagens.Select((contagem, indice) => (Contagem: contagem, Indice: indice))
            .OrderBy(c => c.Contagem.Chave, ComparadorChave.Instancia)
            .ToArray();
        var primeira = ordem[0];
        var segunda = ordem.Length > 1 ? ordem[1] : default;

        try
        {
            await using var contexto = new ContextoBancoDados(opcoesContexto.Value);
            var linhas = await contexto.Database.SqlQueryRaw<LinhaRecusa>(
                    Lote,
                    Chave("@k1", primeira.Contagem.Chave),
                    new SqlParameter("@l1", primeira.Contagem.Limite),
                    new SqlParameter("@j1", primeira.Contagem.JanelaSegundos),
                    Chave("@k2", segunda.Contagem?.Chave),
                    new SqlParameter("@l2", segunda.Contagem?.Limite ?? 0),
                    new SqlParameter("@j2", segunda.Contagem?.JanelaSegundos ?? 0))
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return [.. linhas.Select(l => new RecusaContagem(
                ordem[l.Posicao - 1].Indice,
                TimeSpan.FromMilliseconds(l.EsperaMs),
                l.PrimeiroBloqueio))];
        }
        catch (SqlException excecao) when (excecao.Number == NumeroDeadlock)
        {
            throw new DisputaContagemException("Deadlock na contagem do limite de requisições.", excecao);
        }
    }

    /// <summary>
    /// Abre o pool e prepara o EF Core, para a primeira requisição depois da partida não passar do
    /// tempo máximo e abrir a pausa sem haver falha (R-04).
    /// </summary>
    public async Task AquecerAsync(CancellationToken cancellationToken)
    {
        await using var contexto = new ContextoBancoDados(opcoesContexto.Value);
        await contexto.Database.SqlQueryRaw<int>("SELECT 1 AS Value").ToListAsync(cancellationToken);
    }

    private static string StringConexao(string origem) => new SqlConnectionStringBuilder(origem)
    {
        ApplicationName = NomeAplicacao,
        MaxPoolSize = MaximoConexoes,
        MinPoolSize = MinimoConexoes
    }.ConnectionString;

    private static SqlParameter Chave(string nome, byte[]? valor) =>
        new(nome, SqlDbType.Binary, ContagemLimite.TamanhoChave) { Value = (object?)valor ?? DBNull.Value };

    private sealed class ComparadorChave : IComparer<byte[]>
    {
        public static readonly ComparadorChave Instancia = new();

        public int Compare(byte[]? x, byte[]? y) => x.AsSpan().SequenceCompareTo(y);
    }

    /// <summary>Uma linha do resultado do lote; lida pelo EF Core pelos nomes das colunas.</summary>
    private sealed class LinhaRecusa
    {
        public int Posicao { get; set; }

        public int EsperaMs { get; set; }

        public bool PrimeiroBloqueio { get; set; }
    }
}
