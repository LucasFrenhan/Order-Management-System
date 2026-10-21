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

    public static IResult ParaProblema(this Falha falha)
    {
        var status = falha.Tipo switch
        {
            TipoFalha.Validacao => StatusCodes.Status400BadRequest,
            TipoFalha.NaoAutenticado => StatusCodes.Status401Unauthorized,
            TipoFalha.SemPermissao => StatusCodes.Status403Forbidden,
            TipoFalha.NaoEncontrado => StatusCodes.Status404NotFound,
            TipoFalha.Conflito => StatusCodes.Status409Conflict,
            TipoFalha.RegraNegocio => StatusCodes.Status422UnprocessableEntity,
            TipoFalha.NaoImplementado => StatusCodes.Status501NotImplemented,
            _ => StatusCodes.Status500InternalServerError
        };

        Dictionary<string, object?>? extensoes = falha.Codigo is null ? null : new() { ["codigo"] = falha.Codigo };
        return TypedResults.Problem(detail: falha.Detalhe, statusCode: status, title: falha.Titulo, extensions: extensoes);
    }
}
