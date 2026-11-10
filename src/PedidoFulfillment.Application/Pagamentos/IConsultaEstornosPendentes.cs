namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Pedidos com estorno pendente para a rodada (REQ-13, R-04): primeiro os com tentativa forçada pela
/// operação (REQ-28, R-04); depois os que nunca falharam, do
/// mais antigo para o mais novo; depois os que já falharam, o tentado há mais tempo primeiro.
/// </summary>
public interface IConsultaEstornosPendentes
{
    Task<IReadOnlyList<EstornoPendente>> ListarAsync(int quantidade, CancellationToken cancellationToken);

    /// <summary>
    /// Quantos estão pendentes e quantos passaram do limite de alerta, pela regra do
    /// <see cref="RegrasAlertaEstorno"/>, para o medidor (REQ-28, FR-022a).
    /// </summary>
    Task<(int Pendentes, int AlemDoLimite)> ContarAsync(ParametrosAlertaEstorno parametros, DateTimeOffset agora, CancellationToken cancellationToken);
}
