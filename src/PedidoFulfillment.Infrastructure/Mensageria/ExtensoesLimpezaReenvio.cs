using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Registro da limpeza de mensageria, do executor de reenvios e do aviso da DLQ, só no Worker
/// (REQ-14). Depois de <c>AdicionarMensageria</c> e dos consumidores.
/// </summary>
public static class ExtensoesLimpezaReenvio
{
    public static IServiceCollection AdicionarLimpezaEReenvio(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<OpcoesLimpeza>, ValidadorOpcoesLimpeza>();
        services.AddOptions<OpcoesLimpeza>().BindConfiguration(OpcoesLimpeza.Secao).ValidateOnStart();
        services.AddSingleton<IValidateOptions<OpcoesReenvioDlq>, ValidadorOpcoesReenvioDlq>();
        services.AddOptions<OpcoesReenvioDlq>().BindConfiguration(OpcoesReenvioDlq.Secao).ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<LimpezaMensageria>();
        services.AddSingleton<ReenvioDlq>();
        services.AddHostedService<ExecutorReenviosDlq>();
        return services;
    }
}
