using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Notificacoes;

/// <summary>Registro das notificações de mudança de status (REQ-18).</summary>
public static class ExtensoesNotificacoes
{
    /// <summary>
    /// O que a API e o Worker usam: as opções validadas na partida (sem a chave de cifra, nenhum dos
    /// dois sobe) e a leitura dos avisos.
    /// </summary>
    public static IServiceCollection AdicionarNotificacoes(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<OpcoesNotificacoes>, ValidadorOpcoesNotificacoes>();
        services.AddOptions<OpcoesNotificacoes>().BindConfiguration(OpcoesNotificacoes.Secao).ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<LeituraAvisos>();
        return services;
    }
}
