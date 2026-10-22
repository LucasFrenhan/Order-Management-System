using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Mensageria;
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
        services.AddHostedService<DespachanteEventosSaida>();
        return services;
    }

    /// <summary>
    /// Registra um consumidor de evento no Worker (REQ-06; issues #8 a #10): o manipulador por
    /// escopo e a base que cuida de fila, novas tentativas, DLQ e duplicados.
    /// </summary>
    public static IServiceCollection AdicionarConsumidor<TManipulador, TDados>(this IServiceCollection services)
        where TManipulador : class, IManipuladorEvento<TDados>
    {
        services.AddScoped<TManipulador>();
        services.AddHostedService<ConsumidorEventos<TManipulador, TDados>>();
        return services;
    }
}
