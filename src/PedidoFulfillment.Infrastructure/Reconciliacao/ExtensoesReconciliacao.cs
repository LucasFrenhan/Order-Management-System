using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Reconciliacao;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Reconciliacao;

/// <summary>Registro da reconciliação de pedidos parados, só no Worker (REQ-12, R-09).</summary>
public static class ExtensoesReconciliacao
{
    /// <summary>Depois dos consumidores: as regras das etapas vêm do registro deles (R-04).</summary>
    public static IServiceCollection AdicionarReconciliacao(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<OpcoesReconciliacao>, ValidadorOpcoesReconciliacao>();
        services.AddOptions<OpcoesReconciliacao>().BindConfiguration(OpcoesReconciliacao.Secao).ValidateOnStart();
        services.TryAddSingleton<RegistroFalhasReconciliacao>();
        services.AddScoped<IConsultaPedidosParados, ConsultaPedidosParados>();
        services.AddScoped<ServicoReconciliacao>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<ReconciliadorPedidos>();
        return services;
    }
}
