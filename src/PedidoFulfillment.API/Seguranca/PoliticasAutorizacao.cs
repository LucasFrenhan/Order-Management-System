using Microsoft.AspNetCore.Authorization;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.API.Seguranca;

/// <summary>
/// Policies de autorização (research R-06). Regras de dono do pedido e de transição de
/// status são de negócio e ficam no serviço de aplicação, não aqui.
/// </summary>
public static class PoliticasAutorizacao
{
    public const string PoliticaCliente = nameof(PoliticaCliente);
    public const string PoliticaConsultaPedido = nameof(PoliticaConsultaPedido);
    public const string PoliticaAtualizacaoStatusPedido = nameof(PoliticaAtualizacaoStatusPedido);

    /// <summary>Despacho e correção do rastreio: só a operação (REQ-11, R-08).</summary>
    public const string PoliticaRastreioPedido = nameof(PoliticaRastreioPedido);

    /// <summary>Rotas administrativas da mensageria (REQ-14): só o Admin.</summary>
    public const string PoliticaAdministracao = nameof(PoliticaAdministracao);

    private static readonly string[] TodosOsPerfis = Enum.GetNames<PerfilUsuario>();

    public static IServiceCollection AdicionarPoliticasAutorizacao(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaCliente, p => p.RequireAuthenticatedUser().RequireRole(nameof(PerfilUsuario.Cliente)))
            .AddPolicy(PoliticaConsultaPedido, p => p.RequireAuthenticatedUser().RequireRole(TodosOsPerfis))
            .AddPolicy(PoliticaAtualizacaoStatusPedido, p => p.RequireAuthenticatedUser().RequireRole(TodosOsPerfis))
            .AddPolicy(PoliticaRastreioPedido, p => p.RequireAuthenticatedUser()
                .RequireRole(nameof(PerfilUsuario.Admin), nameof(PerfilUsuario.OperadorLogistica)))
            .AddPolicy(PoliticaAdministracao, p => p.RequireAuthenticatedUser().RequireRole(nameof(PerfilUsuario.Admin)))
            // FR-007a: qualquer endereço sem política explícita exige autenticação,
            // inclusive os inexistentes (401 sem token, 404 com token).
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
