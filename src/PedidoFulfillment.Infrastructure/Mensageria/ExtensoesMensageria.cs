using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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
        return services;
    }
}
