namespace PedidoFulfillment.API.Erros;

/// <summary>
/// Valores vindos da requisição antes de irem para um log (REQ-26, alerta <c>cs/log-forging</c> do
/// CodeQL): sem quebras de linha, ninguém forja uma linha de log nova com um caminho como
/// <c>/x%0A[ERRO] falso</c>, que o <see cref="PathString"/> já entrega decodificado.
/// </summary>
public static class TextoDeLog
{
    public static string SemQuebrasDeLinha(string? valor) =>
        (valor ?? string.Empty)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
}
