using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Emails;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Emails;

/// <summary>Registro do envio de e-mail, só no Worker (REQ-24, R-04). A fila fica na <c>AdicionarInfraestrutura</c>.</summary>
public static class ExtensoesEmails
{
    public static IServiceCollection AdicionarProcessamentoEmails(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<OpcoesEmail>>(provedor =>
            new ValidadorOpcoesEmail(provedor.GetRequiredService<IHostEnvironment>().EnvironmentName));
        services.AddOptions<OpcoesEmail>().BindConfiguration(OpcoesEmail.Secao).ValidateOnStart();
        services.AddScoped<IEnviadorEmail, EnviadorEmailSmtp>();
        services.AddHostedService<ProcessadorEmails>();
        return services;
    }
}
