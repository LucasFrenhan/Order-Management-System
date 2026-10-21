using PedidoFulfillment.API.Erros;
using PedidoFulfillment.API.OpenApi;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Contratos.Autenticacao;

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
            .Produces<UsuarioResponseDTO>(StatusCodes.Status201Created)
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status409Conflict);

        grupo.MapPost("/login", async (LoginRequestDTO credenciais, IServicoAutenticacao servico, CancellationToken cancellationToken) =>
                (await servico.AutenticarAsync(credenciais, cancellationToken)).ParaResposta(token => TypedResults.Ok(token)))
            .WithName("AutenticarUsuario")
            .WithSummary("Autentica com e-mail e senha e emite um token de acesso.")
            .AddEndpointFilter<FiltroValidacao<LoginRequestDTO>>()
            .Produces<LoginResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status401Unauthorized);

        return app;
    }
}
