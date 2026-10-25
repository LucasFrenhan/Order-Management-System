using Microsoft.Data.SqlClient;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// Lock de aplicação do SQL Server por sessão (REQ-12, R-08), para uma instância do Worker por
/// rodada. A conexão precisa ficar aberta até o fim da rodada, e o lock é liberado com
/// <c>sp_releaseapplock</c>: a conexão volta ao pool sem encerrar a sessão.
/// </summary>
internal static class LockAplicacao
{
    public static async Task<bool> AdquirirAsync(SqlConnection conexao, string recurso, CancellationToken cancellationToken)
    {
        await using var comando = new SqlCommand(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;",
            conexao);
        comando.Parameters.AddWithValue("@recurso", recurso);
        return (int)(await comando.ExecuteScalarAsync(cancellationToken))! >= 0;
    }

    public static async Task LiberarAsync(SqlConnection conexao, string recurso)
    {
        try
        {
            await using var comando = new SqlCommand("EXEC sp_releaseapplock @Resource = @recurso, @LockOwner = 'Session';", conexao);
            comando.Parameters.AddWithValue("@recurso", recurso);
            await comando.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (SqlException)
        {
            // Sessão já encerrada: o lock foi junto.
        }
    }
}
