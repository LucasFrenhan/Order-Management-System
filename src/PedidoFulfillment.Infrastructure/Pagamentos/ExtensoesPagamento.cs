using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    /// <summary>
    /// Rodada dos estornos pendentes, só no Worker (REQ-13, R-04 e R-09), com as opções validadas
    /// na partida. Depende do gateway registrado antes.
    /// </summary>
    public static IServiceCollection AdicionarProcessamentoEstornos(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<OpcoesEstornos>, ValidadorOpcoesEstornos>();
        services.AddOptions<OpcoesEstornos>().BindConfiguration(OpcoesEstornos.Secao).ValidateOnStart();
        services.AddScoped<IConsultaEstornosPendentes, ConsultaEstornosPendentes>();
        services.AddScoped<ServicoEstornos>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<ProcessadorEstornos>();
        return services;
    }
}
