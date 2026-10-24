namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Pedidos com estorno pendente para a rodada (REQ-13, R-04): primeiro os que nunca falharam, do
/// mais antigo para o mais novo; depois os que já falharam, o tentado há mais tempo primeiro.
/// </summary>
public interface IConsultaEstornosPendentes
{
    Task<IReadOnlyList<Guid>> ListarAsync(int quantidade, CancellationToken cancellationToken);
}
