using System.Reflection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Um <see cref="SqlException"/> com o número de erro escolhido, para os caminhos que não dá para
/// provocar de forma determinística no banco real (deadlock, 1205). O SqlClient não expõe
/// construtor: os tipos são montados por reflexão, como fazem os testes do próprio SqlClient.
/// </summary>
internal static class ErroSqlSimulado
{
    public const int Deadlock = 1205;

    public static SqlException Criar(int numero, string mensagem = "Erro simulado pelo teste.")
    {
        const BindingFlags Internos = BindingFlags.NonPublic | BindingFlags.Instance;
        var erro = typeof(SqlError).GetConstructors(Internos)
            .Where(c => c.GetParameters() is [{ ParameterType: var primeiro }, ..] && primeiro == typeof(int))
            .OrderBy(c => c.GetParameters().Length)
            .Select(c => (SqlError)c.Invoke(Argumentos(c, numero, mensagem)))
            .First();

        var colecao = (SqlErrorCollection)typeof(SqlErrorCollection).GetConstructors(Internos).Single(c => c.GetParameters().Length == 0).Invoke(null);
        typeof(SqlErrorCollection).GetMethod("Add", Internos)!.Invoke(colecao, [erro]);
        return (SqlException)typeof(SqlException)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(m => m.Name == "CreateException" && m.GetParameters().Select(p => p.ParameterType).SequenceEqual([typeof(SqlErrorCollection), typeof(string)]))
            .Invoke(null, [colecao, "16.0"])!;
    }

    private static object?[] Argumentos(ConstructorInfo construtor, int numero, string mensagem) =>
        [.. construtor.GetParameters().Select((p, i) => i == 0 ? numero : Padrao(p, mensagem))];

    private static object? Padrao(ParameterInfo parametro, string mensagem) => parametro.ParameterType switch
    {
        var t when t == typeof(string) => parametro.Name == "errorMessage" ? mensagem : "",
        var t when t == typeof(byte) => (byte)0,
        var t when t == typeof(int) => 0,
        var t when t == typeof(uint) => 0u,
        _ => null
    };
}

/// <summary>
/// Faz a próxima gravação do contexto falhar com <see cref="DbUpdateException"/> causada pelo
/// <see cref="SqlException"/> informado, antes de chegar ao banco.
/// </summary>
internal sealed class FalhaGravacaoSimulada(SqlException erro) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
        throw new DbUpdateException("Falha simulada pelo teste.", erro);
}
