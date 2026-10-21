using System.Diagnostics;

namespace PedidoFulfillment.API.Erros;

/// <summary>
/// Formato padrão de erro RFC 7807 (FR-009): toda resposta 4xx/5xx sai como
/// <c>application/problem+json</c> com <c>instance</c> e <c>traceId</c>.
/// </summary>
public static class ConfiguracaoProblemDetails
{
    public static IServiceCollection AdicionarProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(opcoes => opcoes.CustomizeProblemDetails = contexto =>
        {
            var problema = contexto.ProblemDetails;
            problema.Instance ??= contexto.HttpContext.Request.Path;
            problema.Type ??= "about:blank";
            problema.Title ??= TituloPadrao(problema.Status);
            problema.Extensions["traceId"] = Activity.Current?.Id ?? contexto.HttpContext.TraceIdentifier;
        });
        services.AddExceptionHandler<ManipuladorExcecoes>();
        return services;
    }

    public static WebApplication UsarProblemDetails(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        return app;
    }

    private static string TituloPadrao(int? status) => status switch
    {
        400 => "Requisição inválida.",
        401 => "Não autenticado.",
        403 => "Acesso negado.",
        404 => "Recurso não encontrado.",
        405 => "Método não permitido.",
        409 => "Conflito com o estado atual do recurso.",
        415 => "Tipo de conteúdo não suportado.",
        422 => "Requisição rejeitada por regra de negócio.",
        501 => "Operação ainda não implementada.",
        _ => "Erro interno."
    };
}
