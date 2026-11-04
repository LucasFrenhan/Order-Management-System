using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.LimiteRequisicoes;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.LimiteRequisicoes;

/// <summary>Registro do limite de requisições, só na API: o Worker não usa a chave (REQ-23).</summary>
public static class ExtensoesLimiteRequisicoes
{
    public static IServiceCollection AdicionarLimiteRequisicoes(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<OpcoesLimiteRequisicoes>, ValidadorOpcoesLimiteRequisicoes>();
        services.AddOptions<OpcoesLimiteRequisicoes>().BindConfiguration(OpcoesLimiteRequisicoes.Secao).ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(provedor => provedor.GetRequiredService<IOptions<OpcoesLimiteRequisicoes>>().Value.ParaConfiguracao());
        services.AddSingleton<IArmazenamentoContagens, ArmazenamentoContagensSql>();
        services.AddSingleton<IBloqueiosLocais, BloqueiosLocaisMemoria>();
        services.AddSingleton<IMetricasLimite, MetricasLimite>();
        services.AddSingleton<IServicoLimiteRequisicoes, ServicoLimiteRequisicoes>();
        services.AddHostedService<AquecimentoLimiteRequisicoes>();
        return services;
    }
}
