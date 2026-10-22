using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PedidoFulfillment.Infrastructure.Persistencia;
using Testcontainers.MsSql;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

[CollectionDefinition(nameof(ColecaoBancoDados))]
public sealed class ColecaoBancoDados : ICollectionFixture<BancoDadosTeste>;

/// <summary>
/// Um SQL Server real em contêiner, compartilhado pelas classes da coleção, com um banco novo
/// por teste (research R-11). Sem Docker, os testes são ignorados.
/// </summary>
public sealed class BancoDadosTeste : IAsyncLifetime
{
    private MsSqlContainer? sqlServer;

    public bool DockerDisponivel { get; private set; }

    /// <summary>String de conexão para um banco ainda inexistente, de nome único.</summary>
    public string NovoBanco() =>
        new SqlConnectionStringBuilder(sqlServer!.GetConnectionString()) { InitialCatalog = $"oms_{Guid.NewGuid():N}" }.ConnectionString;

    /// <summary>Banco novo, já com todas as migrations.</summary>
    public async Task<string> NovoBancoMigradoAsync()
    {
        var stringConexao = NovoBanco();
        await AplicarMigrationsAsync(stringConexao);
        return stringConexao;
    }

    public static ContextoBancoDados NovoContexto(string stringConexao) =>
        new(new DbContextOptionsBuilder<ContextoBancoDados>().UseSqlServer(stringConexao).Options);

    /// <summary>Cria o banco e aplica as migrations sem passar pela API.</summary>
    public static async Task AplicarMigrationsAsync(string stringConexao)
    {
        await using var contexto = NovoContexto(stringConexao);
        await contexto.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Cria o banco e aplica as migrations só até <paramref name="migration"/>, inclusive.</summary>
    public static async Task MigrarAteAsync(string stringConexao, string migration)
    {
        await using var contexto = NovoContexto(stringConexao);
        await contexto.GetService<IMigrator>().MigrateAsync(migration, TestContext.Current.CancellationToken);
    }

    public static async Task<T> ConsultarAsync<T>(string stringConexao, string sql)
    {
        await using var conexao = new SqlConnection(stringConexao);
        await conexao.OpenAsync(TestContext.Current.CancellationToken);
        await using var comando = new SqlCommand(sql, conexao);
        return (T)(await comando.ExecuteScalarAsync(TestContext.Current.CancellationToken))!;
    }

    /// <summary>Executa SQL direto, para gravar o que o domínio não deixaria (SC-004).</summary>
    public static async Task<int> ExecutarAsync(string stringConexao, string sql)
    {
        await using var conexao = new SqlConnection(stringConexao);
        await conexao.OpenAsync(TestContext.Current.CancellationToken);
        await using var comando = new SqlCommand(sql, conexao) { CommandTimeout = 120 };
        return await comando.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Consulta no <c>master</c>, para bancos que podem não existir.</summary>
    public async Task<T> ConsultarServidorAsync<T>(string sql) => await ConsultarAsync<T>(sqlServer!.GetConnectionString(), sql);

    public async ValueTask InitializeAsync()
    {
        try
        {
            sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04").Build();
            await sqlServer.StartAsync();
            DockerDisponivel = true;
        }
        catch (DockerUnavailableException)
        {
            DockerDisponivel = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (sqlServer is not null)
        {
            await sqlServer.DisposeAsync();
        }
    }
}
