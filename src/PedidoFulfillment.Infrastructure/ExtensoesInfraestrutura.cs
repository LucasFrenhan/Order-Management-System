using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PedidoFulfillment.Infrastructure;

/// <summary>
/// Ponto único de registro da Infrastructure, chamado uma vez pela API (FR-004). O REQ-04
/// (persistência) e o REQ-06 (mensageria) acrescentam seus registros aqui.
/// </summary>
public static class ExtensoesInfraestrutura
{
    public static IServiceCollection AdicionarInfraestrutura(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }
}
