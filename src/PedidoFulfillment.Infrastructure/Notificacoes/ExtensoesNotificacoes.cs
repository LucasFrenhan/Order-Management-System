using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Notificacoes;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Notificacoes;

/// <summary>Registro das notificações de mudança de status (REQ-18).</summary>
public static class ExtensoesNotificacoes
{
    /// <summary>
    /// O que a API e o Worker usam: as opções validadas na partida (sem a chave de cifra, nenhum dos
    /// dois sobe), a leitura dos avisos, os destinos e a cifra dos segredos.
    /// </summary>
    public static IServiceCollection AdicionarNotificacoes(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<OpcoesNotificacoes>, ValidadorOpcoesNotificacoes>();
        services.AddOptions<OpcoesNotificacoes>().BindConfiguration(OpcoesNotificacoes.Secao).ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<LeituraAvisos>();
        services.AddScoped<IArmazenamentoNotificacoes, ArmazenamentoNotificacoes>();
        services.AddSingleton<IVerificadorEndereco, VerificadorEndereco>();
        services.AddSingleton<ICifraSegredos, CifraSegredos>();
        services.AddSingleton(provedor =>
        {
            var opcoes = provedor.GetRequiredService<IOptions<OpcoesNotificacoes>>().Value;
            return new ConfiguracaoDestinos(opcoes.DestinosPorCliente, opcoes.DestinosGlobais, opcoes.PermitirRedeLocal);
        });
        return services;
    }

    /// <summary>
    /// Eventos em tempo real (SSE), só na API (REQ-18, research R-16): a leitura periódica do banco,
    /// uma por instância, o distribuidor das conexões e o fluxo de cada conexão.
    /// </summary>
    public static IServiceCollection AdicionarEventosTempoReal(this IServiceCollection services)
    {
        services.AddSingleton<DistribuidorAvisos>();
        services.AddHostedService<LeitorAvisos>();
        services.AddScoped(provedor => new FluxoEventosTempoReal(
            provedor.GetRequiredService<LeituraAvisos>(),
            provedor.GetRequiredService<DistribuidorAvisos>(),
            provedor.GetRequiredService<IOptions<OpcoesNotificacoes>>(),
            provedor.GetRequiredService<TimeProvider>(),
            provedor.GetRequiredService<ILogger<FluxoEventosTempoReal>>()));
        return services;
    }

    /// <summary>
    /// Entrega dos webhooks, só no Worker (REQ-18, R-05 revista): o cliente HTTP próprio, sem
    /// redirecionamento, sem <c>traceparent</c> e com o IP conferido na conexão, e a rotina periódica.
    /// </summary>
    public static IServiceCollection AdicionarProcessamentoNotificacoes(this IServiceCollection services)
    {
        services.AddHttpClient(EnviadorWebhook.NomeCliente, (provedor, cliente) =>
            {
                cliente.Timeout = provedor.GetRequiredService<IOptions<OpcoesNotificacoes>>().Value.Envio.TempoLimite;
                cliente.DefaultRequestHeaders.UserAgent.ParseAdd("OMS-Webhooks/1");
            })
            .ConfigurePrimaryHttpMessageHandler(provedor =>
            {
                var logger = provedor.GetRequiredService<ILogger<EnviadorWebhook>>();
                return EnviadorWebhook.Handler(
                    provedor.GetRequiredService<IOptions<OpcoesNotificacoes>>().Value.PermitirRedeLocal,
                    (host, endereco) => EventosNotificacao.EnderecoRecusado(logger, host, endereco.ToString()));
            });
        services.AddSingleton<EnviadorWebhook>();
        services.AddHostedService<ProcessadorNotificacoes>();
        return services;
    }
}
