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
    private const int ViolacaoRestricao = 547;
    private const int VitimaDeadlock = 1205;

    public static bool EhViolacaoUnicidade(Exception excecao) =>
        Cadeia(excecao).OfType<SqlException>().Any(e => e.Number is ViolacaoIndiceUnico or ViolacaoChaveUnica);

    /// <summary>Violação de unicidade da restrição <paramref name="restricao"/>, identificada pelo nome na mensagem.</summary>
    public static bool EhViolacaoUnicidade(Exception excecao, string restricao) =>
        Cadeia(excecao).OfType<SqlException>().Any(e =>
            e.Number is ViolacaoIndiceUnico or ViolacaoChaveUnica && e.Message.Contains(restricao, StringComparison.Ordinal));

    /// <summary>A transação foi escolhida como vítima de deadlock e desfeita.</summary>
    public static bool EhDeadlock(Exception excecao) =>
        Cadeia(excecao).OfType<SqlException>().Any(e => e.Number == VitimaDeadlock);

    /// <summary>Violação de check constraint ou de chave estrangeira.</summary>
    public static bool EhViolacaoRestricao(Exception excecao) =>
        Cadeia(excecao).OfType<SqlException>().Any(e => e.Number == ViolacaoRestricao);

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
