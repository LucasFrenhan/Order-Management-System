using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Identidade;
using PedidoFulfillment.Infrastructure.Persistencia;
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

        AdicionarIdentidade(services);
        return services;
    }

    private static void AdicionarIdentidade(IServiceCollection services)
    {
        services.AddDbContext<ContextoBancoDados>((provedor, opcoes) =>
            opcoes.UseSqlServer(provedor.GetRequiredService<IOptions<OpcoesSqlServer>>().Value.StringConexao));

        // AddIdentityCore, e não AddIdentity: sem cookies, o esquema padrão segue JWT Bearer (research R-02).
        services.AddIdentityCore<Usuario>(opcoes =>
            {
                opcoes.User.RequireUniqueEmail = true;
                // UserName recebe o e-mail; o contrato decide quais caracteres ele aceita.
                opcoes.User.AllowedUserNameCharacters = "";
                // A regra de senha é a do contrato v1 (R-04); o Identity só garante o mínimo.
                opcoes.Password.RequiredLength = 8;
                opcoes.Password.RequiredUniqueChars = 1;
                opcoes.Password.RequireDigit = false;
                opcoes.Password.RequireLowercase = false;
                opcoes.Password.RequireUppercase = false;
                opcoes.Password.RequireNonAlphanumeric = false;
                opcoes.Lockout.MaxFailedAccessAttempts = 5;
                opcoes.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                opcoes.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<Perfil>()
            .AddEntityFrameworkStores<ContextoBancoDados>();

        services.AddOptions<OpcoesBanco>().BindConfiguration(OpcoesBanco.Secao);
        services.AddHostedService<PreparacaoBancoDados>();
    }
}
