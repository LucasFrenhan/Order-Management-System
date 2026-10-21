using Microsoft.AspNetCore.Mvc;

namespace PedidoFulfillment.API.Erros;

/// <summary>
/// Monta os corpos de erro do contrato: <c>ValidacaoProblemDetails</c> (400, com <c>erros</c>)
/// e <c>NegocioProblemDetails</c> (409/422, com <c>codigo</c>).
/// </summary>
public static class RespostasErro
{
    public const string TituloValidacao = "Um ou mais campos são inválidos.";

    public static ProblemDetails Validacao(IDictionary<string, string[]> erros) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        Title = TituloValidacao,
        Extensions = { ["erros"] = erros }
    };

    public static ProblemDetails Validacao(string chave, string mensagem) =>
        Validacao(new Dictionary<string, string[]> { [chave] = [mensagem] });
}
