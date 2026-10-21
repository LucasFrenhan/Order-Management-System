using System.ComponentModel;
using Microsoft.Data.SqlClient;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// Classificação das exceções do SQL Server, procurando em toda a cadeia de exceções internas.
/// </summary>
internal static class ErrosBancoDados
{
    private const int ViolacaoIndiceUnico = 2601;
    private const int ViolacaoChaveUnica = 2627;

    public static bool EhViolacaoUnicidade(Exception excecao) =>
        Cadeia(excecao).OfType<SqlException>().Any(e => e.Number is ViolacaoIndiceUnico or ViolacaoChaveUnica);

    /// <summary>Servidor inacessível, recusado ou lento demais para responder.</summary>
    public static bool EhFalhaConexao(Exception excecao) =>
        Cadeia(excecao).Any(e => e is SqlException or TimeoutException or Win32Exception);

    private static IEnumerable<Exception> Cadeia(Exception? excecao)
    {
        for (; excecao is not null; excecao = excecao.InnerException)
        {
            yield return excecao;
        }
    }
}
