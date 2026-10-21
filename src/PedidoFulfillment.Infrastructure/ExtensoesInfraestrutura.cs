using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Saude;

namespace PedidoFulfillment.Infrastructure;

/// <summary>
/// Ponto único de registro da Infrastructure, chamado uma vez pela API (FR-004). O REQ-04
/// (persistência) e o REQ-06 (mensageria) acrescentam seus registros aqui.
/// </summary>
public static class ExtensoesInfraestrutura
{
    /// <summary>Tag das verificações executadas por <c>/health/pronto</c>.</summary>
    public const string TagPronto = "pronto";

    private static readonly TimeSpan LimiteVerificacao = TimeSpan.FromSeconds(5);

    public static IServiceCollection AdicionarInfraestrutura(this IServiceCollection services, IConfiguration configuration)
    {
        // Validadas na inicialização em todos os ambientes; sem elas a API não sobe (FR-012).
        services.AddSingleton<IValidateOptions<OpcoesSqlServer>, ValidadorOpcoesSqlServer>();
        services.AddSingleton<IValidateOptions<OpcoesRabbitMQ>, ValidadorOpcoesRabbitMQ>();
        services.AddOptions<OpcoesSqlServer>()
            .Configure(o => o.StringConexao = configuration.GetConnectionString(OpcoesSqlServer.NomeConexao) ?? "")
            .ValidateOnStart();
        services.AddOptions<OpcoesRabbitMQ>()
            .BindConfiguration(OpcoesRabbitMQ.Secao)
            .ValidateOnStart();

        services.AddHealthChecks()
            .AddCheck<VerificacaoSqlServer>("sqlserver", tags: [TagPronto], timeout: LimiteVerificacao)
            .AddCheck<VerificacaoRabbitMQ>("rabbitmq", tags: [TagPronto], timeout: LimiteVerificacao);

        return services;
    }
}
