using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Autenticacao;

/// <summary>Registro das sessões, só na API (REQ-24): o Worker não emite nem renova.</summary>
public static class ExtensoesSessoes
{
    public static IServiceCollection AdicionarSessoes(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<OpcoesSessoes>, ValidadorOpcoesSessoes>();
        services.AddOptions<OpcoesSessoes>().BindConfiguration(OpcoesSessoes.Secao).ValidateOnStart();
        services.AddSingleton(provedor => provedor.GetRequiredService<IOptions<OpcoesSessoes>>().Value.ParaConfiguracao());
        services.AddSingleton<IMetricasAutenticacao, MetricasAutenticacao>();
        return services;
    }
}
