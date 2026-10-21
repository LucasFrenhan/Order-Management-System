using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PedidoFulfillment.API.Seguranca;

/// <summary>
/// Configuração da seção <c>Jwt</c>. A emissão de tokens é do REQ-03; aqui só a validação.
/// </summary>
public sealed class OpcoesJwt
{
    public const string Secao = "Jwt";

    public string Emissor { get; set; } = "";

    public string Audiencia { get; set; } = "";

    public string ChaveAssinatura { get; set; } = "";

    public int DuracaoTokenMinutos { get; set; } = 60;
}

/// <summary>
/// Validação de JWT Bearer (R-06) com respostas 401/403 em ProblemDetails (R-07).
/// </summary>
public static class ConfiguracaoJwt
{
    public const string ClaimPerfil = "role";
    public const string ClaimUsuario = "sub";

    public static IServiceCollection AdicionarAutenticacaoJwt(this IServiceCollection services)
    {
        services.AddOptions<OpcoesJwt>()
            .BindConfiguration(OpcoesJwt.Secao)
            .Validate(o => Encoding.UTF8.GetByteCount(o.ChaveAssinatura) >= 32,
                "Jwt:ChaveAssinatura deve ter ao menos 32 bytes.")
            .Validate(o => o.Emissor.Length > 0 && o.Audiencia.Length > 0, "Jwt:Emissor e Jwt:Audiencia são obrigatórios.")
            .ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<OpcoesJwt>>((opcoes, jwt) =>
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
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.ChaveAssinatura)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    NameClaimType = ClaimUsuario,
                    RoleClaimType = ClaimPerfil
                };
                opcoes.Events = new JwtBearerEvents
                {
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
