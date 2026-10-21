using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PedidoFulfillment.API.Erros;

/// <summary>
/// Converte exceções em ProblemDetails sem expor detalhes internos (R-07, R-08).
/// Falhas de binding viram 400 com <c>erros</c> (FR-010); o resto vira 500.
/// </summary>
public sealed partial class ManipuladorExcecoes(IProblemDetailsService problemDetails, ILogger<ManipuladorExcecoes> logger)
    : IExceptionHandler
{
    private const string MensagemCorpoInvalido = "O corpo da requisição está ausente ou não é um JSON válido.";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ProblemDetails problema;
        if (exception is BadHttpRequestException falhaRequisicao)
        {
            problema = falhaRequisicao.StatusCode == StatusCodes.Status400BadRequest
                ? RespostasErro.Validacao(ErrosDeBinding(httpContext, falhaRequisicao))
                : new ProblemDetails { Status = falhaRequisicao.StatusCode };
        }
        else
        {
            logger.LogError(exception, "Erro não tratado ao processar {Metodo} {Caminho}.",
                httpContext.Request.Method, httpContext.Request.Path);
            problema = new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = "Erro interno." };
        }

        httpContext.Response.StatusCode = problema.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problema,
            Exception = exception
        });
    }

    private static Dictionary<string, string[]> ErrosDeBinding(HttpContext httpContext, BadHttpRequestException excecao)
    {
        if (excecao.InnerException is JsonException json)
        {
            var chave = CaminhoJson(json.Path);
            return new() { [chave] = [chave.Length == 0 ? MensagemCorpoInvalido : "Valor inválido ou ausente."] };
        }

        var parametro = ParametroDoHandler(httpContext, excecao.Message);
        if (parametro is null || parametro.GetCustomAttribute<FromBodyAttribute>() is not null || EhCorpo(excecao.Message))
        {
            return new() { [""] = [MensagemCorpoInvalido] };
        }

        var cabecalho = parametro.GetCustomAttribute<FromHeaderAttribute>();
        if (cabecalho is not null)
        {
            var nome = cabecalho.Name ?? parametro.Name!;
            return new() { [nome] = [$"O cabeçalho {nome} é obrigatório e deve ser um UUID."] };
        }

        return new() { [parametro.Name!] = ["O identificador deve ser um UUID."] };
    }

    private static ParameterInfo? ParametroDoHandler(HttpContext httpContext, string mensagem)
    {
        var correspondencia = ParametroNaMensagem().Match(mensagem);
        if (!correspondencia.Success)
        {
            return null;
        }

        // O middleware de exceções limpa o endpoint do contexto; o original fica na feature.
        var endpoint = httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint ?? httpContext.GetEndpoint();
        var metodo = endpoint?.Metadata.GetMetadata<MethodInfo>();
        return metodo?.GetParameters().FirstOrDefault(p => p.Name == correspondencia.Groups["nome"].Value);
    }

    private static bool EhCorpo(string mensagem) => mensagem.Contains("body", StringComparison.OrdinalIgnoreCase);

    // "$.itens[0].quantidade" -> "itens[0].quantidade"; "$" ou nulo -> "" (corpo inteiro).
    private static string CaminhoJson(string? caminho)
    {
        if (string.IsNullOrEmpty(caminho) || caminho == "$")
        {
            return "";
        }

        var semRaiz = caminho.StartsWith("$.", StringComparison.Ordinal) ? caminho[2..] : caminho.TrimStart('$');
        return string.Join('.', semRaiz.Split('.').Select(parte => parte.Length == 0 ? parte : char.ToLowerInvariant(parte[0]) + parte[1..]));
    }

    // Mensagens do ASP.NET Core: 'Failed to bind parameter "Guid id" from "abc".'
    // e 'Required parameter "Guid chave" was not provided from header.'
    [GeneratedRegex("parameter \"(?:[^\"]*\\s)?(?<nome>\\w+)\"")]
    private static partial Regex ParametroNaMensagem();
}
