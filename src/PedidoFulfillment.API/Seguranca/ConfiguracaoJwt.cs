using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace PedidoFulfillment.API.Seguranca;

/// <summary>
/// Configuração da seção <c>Jwt</c>, usada na validação (abaixo) e na emissão (<see cref="EmissorTokenJwt"/>).
/// Desde o REQ-27, um conjunto de chaves com identificador e a chave ativa, que emite
/// (specs/027-rotacao-chave-jwt/contracts/configuracao.md).
/// </summary>
public sealed class OpcoesJwt
{
    public const string Secao = "Jwt";

    public string Emissor { get; set; } = "";

    public string Audiencia { get; set; } = "";

    public int DuracaoTokenMinutos { get; set; } = 15;

    /// <summary>De 1 a 3 chaves; todas validam, e só a <see cref="ChaveAtiva"/> emite.</summary>
    public List<ChaveJwtConfigurada> Chaves { get; set; } = [];

    /// <summary>O identificador da chave que emite os tokens.</summary>
    public string ChaveAtiva { get; set; } = "";

    /// <summary>Formato anterior ao REQ-27. Só é lido para recusar a partida (FR-007).</summary>
    public string ChaveAssinatura { get; set; } = "";
}

/// <summary>Um item de <c>Jwt:Chaves</c>: o identificador vai no <c>kid</c> do token; o valor é secreto.</summary>
public sealed class ChaveJwtConfigurada
{
    public string Identificador { get; set; } = "";

    public string Valor { get; set; } = "";
}

/// <summary>
/// Validação de JWT Bearer (R-06) com respostas 401/403 em ProblemDetails (R-07). A chave é escolhida pelo
/// <c>kid</c> do token; sem <c>kid</c>, ou com um que não está configurado, o token é recusado (REQ-27, R-02).
/// </summary>
public static class ConfiguracaoJwt
{
    public const string ClaimPerfil = "role";
    public const string ClaimUsuario = "sub";

    public static IServiceCollection AdicionarAutenticacaoJwt(this IServiceCollection services)
    {
        services.AddOptions<OpcoesJwt>().BindConfiguration(OpcoesJwt.Secao).ValidateOnStart();
        services.AddSingleton<IValidateOptions<OpcoesJwt>, ValidadorOpcoesJwt>();
        services.AddSingleton<ConjuntoChavesJwt>();
        services.AddHostedService<RegistroChavesJwtNaPartida>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<OpcoesJwt>, ConjuntoChavesJwt>((opcoes, jwt, chaves) =>
            {
                opcoes.MapInboundClaims = false;
                opcoes.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Value.Emissor,
                    ValidateAudience = true,
                    ValidAudience = jwt.Value.Audiencia,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKeys = chaves.Todas,
                    // Sem isto, um token sem kid (ou com kid desconhecido) seria testado contra todas as chaves.
                    TryAllIssuerSigningKeys = false,
                    IssuerSigningKeyResolver = (_, _, kid, _) => chaves.Buscar(kid) is { } chave ? [chave] : [],
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = ClaimUsuario,
                    RoleClaimType = ClaimPerfil
                };
                opcoes.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = contexto =>
                    {
                        RegistrarChaveDesconhecida(contexto.HttpContext, chaves);
                        return Task.CompletedTask;
                    },
                    OnChallenge = async contexto =>
                    {
                        contexto.HandleResponse();
                        contexto.Response.Headers.WWWAuthenticate = "Bearer";
                        await EscreverProblema(contexto.HttpContext, StatusCodes.Status401Unauthorized, "Não autenticado.",
                            "Informe um token de acesso válido.");
                    },
                    OnForbidden = contexto => EscreverProblema(contexto.HttpContext, StatusCodes.Status403Forbidden,
                        "Acesso negado.", "O usuário não tem permissão para esta operação.")
                };
            });

        return services;
    }

    /// <summary>
    /// Log 3027 quando o token recusado não tem <c>kid</c> ou traz um fora do conjunto (REQ-27, R-05). Lê só o
    /// cabeçalho, sem validar; um token ilegível ou com <c>kid</c> configurado (assinatura errada, expirado) não
    /// gera o 3027. A resposta não muda (FR-004).
    /// </summary>
    private static void RegistrarChaveDesconhecida(HttpContext httpContext, ConjuntoChavesJwt chaves)
    {
        var autorizacao = httpContext.Request.Headers.Authorization.ToString();
        if (!autorizacao.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        JsonWebToken token;
        try
        {
            token = new JsonWebToken(autorizacao["Bearer ".Length..].Trim());
        }
        catch (ArgumentException)
        {
            // Inclui SecurityTokenMalformedException: token ilegível não tem cabeçalho a registrar.
            return;
        }

        string identificador;
        if (token.TryGetHeaderValue<string>(JwtHeaderParameterNames.Kid, out var kid))
        {
            if (chaves.Contem(kid))
            {
                return;
            }

            identificador = ValidadorOpcoesJwt.IdentificadorValido(kid) ? kid : "(inválido)";
        }
        else
        {
            identificador = token.TryGetHeaderValue<object>(JwtHeaderParameterNames.Kid, out _) ? "(inválido)" : "(ausente)";
        }

        var logger = httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ConfiguracaoJwt).FullName!);
        EventosChavesJwt.TokenChaveDesconhecida(logger, identificador);
    }

    private static async Task EscreverProblema(HttpContext httpContext, int status, string titulo, string detalhe)
    {
        httpContext.Response.StatusCode = status;
        var servico = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await servico.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Title = titulo, Detail = detalhe }
        });
    }
}
