using Microsoft.Extensions.Diagnostics.HealthChecks;
using PedidoFulfillment.Infrastructure.Mensageria;

namespace PedidoFulfillment.Infrastructure.Saude;

/// <summary>
/// Prontidão dos consumidores do Worker (REQ-17, FR-013; contracts/recuperacao-consumo.md):
/// pronta só com todos inscritos, não pronta na hora em que algum perde a inscrição, sem
/// tolerância (clarify 2). Lê só memória; sem descrição nem dados, como as outras (REQ-02).
/// </summary>
internal sealed class VerificacaoConsumidores(EstadoConsumidores estado) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(estado.TodosInscritos ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy());
}
