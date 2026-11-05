using System.Security.Claims;
using PedidoFulfillment.API.Erros;
using PedidoFulfillment.API.LimiteRequisicoes;
using PedidoFulfillment.API.OpenApi;
using PedidoFulfillment.API.Seguranca;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.LimiteRequisicoes;

namespace PedidoFulfillment.API.Endpoints;

/// <summary>
/// Rotas de autenticação: as públicas e a troca de senha, que exige token (REQ-24). Só roteiam:
/// validam, delegam ao serviço e traduzem o resultado.
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

        grupo.MapPost("/recuperacao-senha", async (RecuperacaoSenhaRequestDTO pedido, IServicoSenhas servico, CancellationToken cancellationToken) =>
            {
                await servico.PedirRecuperacaoAsync(pedido, cancellationToken);
                return TypedResults.Accepted((string?)null);
            })
            .WithName("PedirRecuperacaoSenha")
            .WithSummary("Pede um e-mail de recuperação de senha.")
            .AddEndpointFilter<FiltroValidacao<RecuperacaoSenhaRequestDTO>>()
            .ComLimiteRequisicoes(RotaLimitada.RecuperacaoSenha)
            .Produces(StatusCodes.Status202Accepted)
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status429TooManyRequests);

        grupo.MapPost("/redefinicao-senha", async (RedefinicaoSenhaRequestDTO redefinicao, IServicoSenhas servico, CancellationToken cancellationToken) =>
                (await servico.RedefinirAsync(redefinicao, cancellationToken)).ParaSemConteudo())
            .WithName("RedefinirSenha")
            .WithSummary("Define uma senha nova com o token recebido por e-mail.")
            .AddEndpointFilter<FiltroValidacao<RedefinicaoSenhaRequestDTO>>()
            .ComLimiteRequisicoes(RotaLimitada.RedefinicaoSenha)
            .Produces(StatusCodes.Status204NoContent)
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status429TooManyRequests)
            .ComErroToken();

        grupo.MapPost("/confirmacao-email", async (ConfirmacaoEmailRequestDTO confirmacao, IServicoConfirmacaoEmail servico, CancellationToken cancellationToken) =>
                (await servico.ConfirmarAsync(confirmacao, cancellationToken)).ParaSemConteudo())
            .WithName("ConfirmarEmail")
            .WithSummary("Confirma o e-mail da conta com o token recebido por e-mail.")
            .AddEndpointFilter<FiltroValidacao<ConfirmacaoEmailRequestDTO>>()
            .ComLimiteRequisicoes(RotaLimitada.ConfirmacaoEmail)
            .Produces(StatusCodes.Status204NoContent)
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status429TooManyRequests)
            .ComErroToken();

        grupo.MapPost("/confirmacao-email/reenvio", async (ReenvioConfirmacaoRequestDTO pedido, IServicoConfirmacaoEmail servico, CancellationToken cancellationToken) =>
            {
                await servico.PedirReenvioAsync(pedido, cancellationToken);
                return TypedResults.Accepted((string?)null);
            })
            .WithName("ReenviarConfirmacaoEmail")
            .WithSummary("Pede um novo e-mail de confirmação.")
            .AddEndpointFilter<FiltroValidacao<ReenvioConfirmacaoRequestDTO>>()
            .ComLimiteRequisicoes(RotaLimitada.ReenvioConfirmacao)
            .Produces(StatusCodes.Status202Accepted)
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status429TooManyRequests);

        // Fora do grupo: o AllowAnonymous dele prevaleceria sobre a política e deixaria a rota pública.
        app.MapPost("/api/v1/auth/troca-senha", async (TrocaSenhaRequestDTO troca, ClaimsPrincipal usuario, IServicoSenhas servico, CancellationToken cancellationToken) =>
                (await servico.TrocarAsync(usuario.ObterUsuarioId(), troca, cancellationToken)).ParaSemConteudo())
            .WithTags("Autenticacao")
            .WithName("TrocarSenha")
            .WithSummary("Troca a senha da pessoa conectada e encerra todas as sessões da conta.")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaContaAutenticada)
            .AddEndpointFilter<FiltroValidacao<TrocaSenhaRequestDTO>>()
            .Produces(StatusCodes.Status204NoContent)
            .ProduzErros(StatusCodes.Status400BadRequest);

        return app;
    }
}
