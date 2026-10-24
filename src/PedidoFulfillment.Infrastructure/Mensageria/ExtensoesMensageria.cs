using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Reconciliacao;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Registro da mensageria, usado só pelo Worker (REQ-06): a API não fala com o broker para
/// publicar; ela grava os eventos no outbox (clarify).
/// </summary>
public static class ExtensoesMensageria
{
    public static IServiceCollection AdicionarMensageria(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<OpcoesMensageria>, ValidadorOpcoesMensageria>();
        services.AddOptions<OpcoesMensageria>().BindConfiguration(OpcoesMensageria.Secao).ValidateOnStart();
        services.TryAddSingleton<ConexaoRabbitMQ>();
        services.TryAddSingleton<TopologiaConhecida>();
        services.AddHostedService<DespachanteEventosSaida>();
        return services;
    }

    /// <summary>
    /// Registra um consumidor de evento no Worker (REQ-06; issues #8 a #10): o manipulador por
    /// escopo e a base que cuida de fila, novas tentativas, DLQ e duplicados. O descritor põe o
    /// consumidor na topologia que o despachante declara antes de publicar (REQ-12, R-01), e a
    /// regra do manipulador, se for uma etapa do fluxo, fica disponível para a reconciliação (R-04).
    /// </summary>
    public static IServiceCollection AdicionarConsumidor<TManipulador, TDados>(this IServiceCollection services)
        where TManipulador : class, IManipuladorEvento<TDados>
    {
        services.AddScoped<TManipulador>();
        services.AddHostedService<ConsumidorEventos<TManipulador, TDados>>();
        services.AddSingleton<IDescritorConsumidor, DescritorConsumidor<TManipulador, TDados>>();
        if (typeof(IEtapaFluxoPedido).IsAssignableFrom(typeof(TManipulador)))
        {
            services.AddScoped(sp => (IEtapaFluxoPedido)sp.GetRequiredService<TManipulador>());
        }

        return services;
    }
}
