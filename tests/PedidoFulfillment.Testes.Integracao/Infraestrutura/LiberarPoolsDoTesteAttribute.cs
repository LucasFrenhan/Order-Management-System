using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Data.SqlClient;
using PedidoFulfillment.Infrastructure.LimiteRequisicoes;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using Xunit.v3;

[assembly: LiberarPoolsDoTeste]

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Fecha, no fim de cada teste, as conexões que sobraram nos pools dos bancos que ele criou (#77).
/// </summary>
/// <remarks>
/// Cada teste tem um banco próprio, e o SqlClient abre um pool para cada string de conexão. Sem
/// isso, as conexões ociosas só fecham depois de 4 a 8 minutos, e as do mínimo do pool
/// (<c>MinPoolSize</c> do armazenamento de limites) nunca fecham: elas se acumulam pela suíte e,
/// no Docker Desktop, passam pelo teto de conexões do <c>wslrelay</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class LiberarPoolsDoTesteAttribute : BeforeAfterTestAttribute
{
    private static readonly ConcurrentDictionary<string, ConcurrentBag<string>> BancosPorTeste = new();

    /// <summary>Nome da aplicação nas strings dos testes.</summary>
    /// <remarks>
    /// Sem ele, o EF Core acrescenta o próprio <c>Application Name</c> à string, e o pool dele fica
    /// com uma chave que o <see cref="SqlConnection.ClearPool"/> não alcança.
    /// </remarks>
    private const string NomeAplicacao = "oms-testes";

    /// <summary>
    /// Anota o banco no teste em andamento, para os pools dele fecharem no fim, e devolve a string
    /// com o nome da aplicação fixo.
    /// </summary>
    public static string Registrar(string stringConexao)
    {
        stringConexao = new SqlConnectionStringBuilder(stringConexao) { ApplicationName = NomeAplicacao }.ConnectionString;
        if (TestContext.Current.Test?.UniqueID is { } teste)
        {
            BancosPorTeste.GetOrAdd(teste, _ => []).Add(stringConexao);
        }

        return stringConexao;
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!BancosPorTeste.TryRemove(test.UniqueID, out var bancos))
        {
            return;
        }

        foreach (var stringConexao in bancos)
        {
            LimparPool(stringConexao);
            LimparPool(ArmazenamentoContagensSql.StringConexao(stringConexao));
        }
    }

    private static void LimparPool(string stringConexao)
    {
        using var conexao = new SqlConnection(stringConexao);
        SqlConnection.ClearPool(conexao);
    }
}
