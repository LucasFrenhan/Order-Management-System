namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Pedidos com estorno pendente para a rodada (REQ-13, R-04): primeiro os com tentativa forçada pela
/// operação (REQ-28, R-04); depois os que nunca falharam, do
/// mais antigo para o mais novo; depois os que já falharam, o tentado há mais tempo primeiro.
/// </summary>
public interface IConsultaEstornosPendentes
{
    Task<IReadOnlyList<EstornoPendente>> ListarAsync(int quantidade, CancellationToken cancellationToken);
}
