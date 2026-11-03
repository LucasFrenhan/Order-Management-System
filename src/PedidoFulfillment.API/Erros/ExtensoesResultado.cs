using PedidoFulfillment.Application.Comum;

namespace PedidoFulfillment.API.Erros;

/// <summary>
/// Traduz <see cref="Resultado{T}"/> em respostas HTTP. Endpoints só chamam o serviço e esta
/// tradução; nenhuma regra de negócio fica aqui.
/// </summary>
public static class ExtensoesResultado
{
    public static IResult ParaResposta<T>(this Resultado<T> resultado, Func<T, IResult> sucesso) =>
        resultado.Sucesso ? sucesso(resultado.Valor!) : resultado.Falha!.ParaProblema();

    /// <param name="statusCode">Outro status para a mesma falha, como o 429 do limite de conexões SSE (REQ-18, FR-023).</param>
    public static IResult ParaProblema(this Falha falha, int? statusCode = null)
    {
        if (falha is { Tipo: TipoFalha.Validacao, Erros: { } erros } && statusCode is null)
        {
            return TypedResults.Problem(RespostasErro.Validacao(erros.ToDictionary(e => e.Key, e => e.Value)));
        }

        var status = statusCode ?? falha.Tipo switch
        {
            TipoFalha.Validacao => StatusCodes.Status400BadRequest,
            TipoFalha.NaoAutenticado => StatusCodes.Status401Unauthorized,
            TipoFalha.SemPermissao => StatusCodes.Status403Forbidden,
            TipoFalha.NaoEncontrado => StatusCodes.Status404NotFound,
            TipoFalha.Conflito => StatusCodes.Status409Conflict,
            TipoFalha.RegraNegocio => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status500InternalServerError
        };

        Dictionary<string, object?>? extensoes = falha.Codigo is null ? null : new() { ["codigo"] = falha.Codigo };
        return TypedResults.Problem(detail: falha.Detalhe, statusCode: status, title: falha.Titulo, extensions: extensoes);
    }
}
