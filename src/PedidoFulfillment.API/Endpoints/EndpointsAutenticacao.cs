using PedidoFulfillment.API.Erros;
using PedidoFulfillment.API.LimiteRequisicoes;
using PedidoFulfillment.API.OpenApi;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.LimiteRequisicoes;

namespace PedidoFulfillment.API.Endpoints;

/// <summary>
/// Rotas públicas de autenticação (User Story 2). Só roteiam: validam, delegam ao serviço e
/// traduzem o resultado.
/// </summary>
public static class EndpointsAutenticacao
{
    public static WebApplication MapearEndpointsAutenticacao(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/v1/auth").WithTags("Autenticacao").AllowAnonymous();

        grupo.MapPost("/register", async (RegistrarUsuarioRequestDTO usuario, IServicoAutenticacao servico, CancellationToken cancellationToken) =>
                (await servico.RegistrarAsync(usuario, cancellationToken)).ParaResposta(criado => TypedResults.Created((string?)null, criado)))
            .WithName("RegistrarUsuario")
            .WithSummary("Registra um novo usuário com o perfil Cliente.")
            .AddEndpointFilter<FiltroValidacao<RegistrarUsuarioRequestDTO>>()
            .ComLimiteRequisicoes(RotaLimitada.Registro)
            .Produces<UsuarioResponseDTO>(StatusCodes.Status201Created)
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status409Conflict, StatusCodes.Status429TooManyRequests);

        grupo.MapPost("/login", async (LoginRequestDTO credenciais, IServicoAutenticacao servico, CancellationToken cancellationToken) =>
                (await servico.AutenticarAsync(credenciais, cancellationToken)).ParaResposta(token => TypedResults.Ok(token)))
            .WithName("AutenticarUsuario")
            .WithSummary("Autentica com e-mail e senha e emite um token de acesso.")
            .AddEndpointFilter<FiltroValidacao<LoginRequestDTO>>()
            .ComLimiteRequisicoes(RotaLimitada.Login)
            .Produces<LoginResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status401Unauthorized, StatusCodes.Status429TooManyRequests);

        grupo.MapPost("/renovacao", async (RenovacaoRequestDTO renovacao, IServicoSessoes servico, CancellationToken cancellationToken) =>
                (await servico.RenovarAsync(renovacao, cancellationToken)).ParaResposta(token => TypedResults.Ok(token)))
            .WithName("RenovarSessao")
            .WithSummary("Troca um token de renovação por um novo par de tokens.")
            .AddEndpointFilter<FiltroValidacao<RenovacaoRequestDTO>>()
            .ComLimiteRequisicoes(RotaLimitada.Renovacao)
            .Produces<LoginResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status401Unauthorized, StatusCodes.Status429TooManyRequests);

        // Sem exigir token de acesso: precisa funcionar também com ele expirado (FR-008).
        grupo.MapPost("/logout", async (LogoutRequestDTO logout, IServicoSessoes servico, CancellationToken cancellationToken) =>
            {
                await servico.EncerrarAsync(logout, cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName("EncerrarSessao")
            .WithSummary("Encerra a sessão do token de renovação informado.")
            .AddEndpointFilter<FiltroValidacao<LogoutRequestDTO>>()
            .ComLimiteRequisicoes(RotaLimitada.Logout)
            .Produces(StatusCodes.Status204NoContent)
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status429TooManyRequests);

        return app;
    }
}
