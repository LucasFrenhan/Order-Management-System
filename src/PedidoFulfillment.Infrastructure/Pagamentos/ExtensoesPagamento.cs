using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Pagamentos;

/// <summary>Registro do gateway de pagamento, usado só pelo Worker (REQ-09, R-02).</summary>
public static class ExtensoesPagamento
{
    /// <summary>Gateway simulado, com a taxa de aprovação validada na partida (FR-005).</summary>
    public static IServiceCollection AdicionarGatewayPagamentoSimulado(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<OpcoesGatewayPagamento>, ValidadorOpcoesGatewayPagamento>();
        services.AddOptions<OpcoesGatewayPagamento>().BindConfiguration(OpcoesGatewayPagamento.Secao).ValidateOnStart();
        services.AddSingleton<IGatewayPagamento, GatewayPagamentoSimulado>();
        return services;
    }
}
