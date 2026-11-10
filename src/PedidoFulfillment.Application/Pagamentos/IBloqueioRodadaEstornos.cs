namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// O mesmo lock da rodada de estornos, pedido pela API (REQ-28, R-03): a baixa não pode ser gravada
/// enquanto a rodada pediu um estorno ao gateway e ainda não gravou o resultado.
/// </summary>
public interface IBloqueioRodadaEstornos
{
    /// <summary>
    /// Espera o lock até <paramref name="espera"/>, executa a ação numa transação que o segura e a
    /// confirma; a ação usa o mesmo armazenamento do escopo.
    /// </summary>
    /// <returns><see langword="false"/> se o lock não veio a tempo; nada foi executado.</returns>
    Task<bool> ExecutarAsync(TimeSpan espera, Func<CancellationToken, Task> acao, CancellationToken cancellationToken);
}
