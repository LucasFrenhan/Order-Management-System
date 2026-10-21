using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Saude;

/// <summary>
/// Prontidão do banco: conecta no catálogo <c>master</c> (o banco da aplicação nasce no
/// REQ-04) e executa <c>SELECT 1</c>, em no máximo 5 segundos (FR-018).
/// </summary>
internal sealed class VerificacaoSqlServer(IOptions<OpcoesSqlServer> opcoes) : IHealthCheck
{
    private const int LimiteSegundos = 5;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(TimeSpan.FromSeconds(LimiteSegundos));
        try
        {
            var stringConexao = new SqlConnectionStringBuilder(opcoes.Value.StringConexao)
            {
                InitialCatalog = "master",
                ConnectTimeout = LimiteSegundos
            };
            await using var conexao = new SqlConnection(stringConexao.ConnectionString);
            await conexao.OpenAsync(limite.Token);
            await using var comando = new SqlCommand("SELECT 1", conexao) { CommandTimeout = LimiteSegundos };
            await comando.ExecuteScalarAsync(limite.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            // Sem descrição nem exceção: nada de dado de conexão pode chegar à resposta.
            return HealthCheckResult.Unhealthy();
        }
    }
}
