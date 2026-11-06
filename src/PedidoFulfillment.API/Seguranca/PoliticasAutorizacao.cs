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

    /// <summary>Destinos de webhook: o Cliente (os próprios) e o Admin (os globais) (REQ-18, FR-007 e FR-011).</summary>
    public const string PoliticaDestinosNotificacao = nameof(PoliticaDestinosNotificacao);

    /// <summary>Gestão do catálogo de produtos: só o Admin (REQ-22, FR-008).</summary>
    public const string PoliticaCatalogo = nameof(PoliticaCatalogo);

    /// <summary>Operações da própria conta, como a troca de senha: qualquer perfil (REQ-24).</summary>
    public const string PoliticaContaAutenticada = nameof(PoliticaContaAutenticada);

    /// <summary>Gestão de usuários e perfis: Admin no token e ainda Admin ativo no banco (REQ-25, R-02).</summary>
    public const string PoliticaGestaoUsuarios = nameof(PoliticaGestaoUsuarios);

    private static readonly string[] TodosOsPerfis = Enum.GetNames<PerfilUsuario>();

    public static IServiceCollection AdicionarPoliticasAutorizacao(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IAuthorizationHandler, ManipuladorAdminAtivo>();
        services.AddAuthorizationBuilder()
            .AddPolicy(PoliticaGestaoUsuarios, p => p.RequireAuthenticatedUser()
                .RequireRole(nameof(PerfilUsuario.Admin))
                .AddRequirements(new RequisitoAdminAtivo()))
            .AddPolicy(PoliticaCliente, p => p.RequireAuthenticatedUser().RequireRole(nameof(PerfilUsuario.Cliente)))
            .AddPolicy(PoliticaContaAutenticada, p => p.RequireAuthenticatedUser().RequireRole(TodosOsPerfis))
            .AddPolicy(PoliticaConsultaPedido, p => p.RequireAuthenticatedUser().RequireRole(TodosOsPerfis))
            .AddPolicy(PoliticaAtualizacaoStatusPedido, p => p.RequireAuthenticatedUser().RequireRole(TodosOsPerfis))
            .AddPolicy(PoliticaRastreioPedido, p => p.RequireAuthenticatedUser()
                .RequireRole(nameof(PerfilUsuario.Admin), nameof(PerfilUsuario.OperadorLogistica)))
            .AddPolicy(PoliticaAdministracao, p => p.RequireAuthenticatedUser().RequireRole(nameof(PerfilUsuario.Admin)))
            .AddPolicy(PoliticaCatalogo, p => p.RequireAuthenticatedUser().RequireRole(nameof(PerfilUsuario.Admin)))
            .AddPolicy(PoliticaDestinosNotificacao, p => p.RequireAuthenticatedUser()
                .RequireRole(nameof(PerfilUsuario.Cliente), nameof(PerfilUsuario.Admin)))
            // FR-007a: qualquer endereço sem política explícita exige autenticação,
            // inclusive os inexistentes (401 sem token, 404 com token).
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        return services;
    }
}
