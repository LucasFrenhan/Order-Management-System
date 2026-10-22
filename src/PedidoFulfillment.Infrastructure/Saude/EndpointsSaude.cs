using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PedidoFulfillment.Infrastructure.Saude;

/// <summary>
/// Endereços de saúde (REQ-02, contracts/saude.md): públicos e fora do contrato v1. A
/// resposta traz só nomes e estados, nunca descrições, exceções ou dados de conexão. Fica na
/// Infrastructure para a API e o Worker usarem o mesmo mapeamento (REQ-06, achado M1).
/// </summary>
public static class EndpointsSaude
{
    public static IEndpointRouteBuilder MapearEndpointsSaude(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate = _ => false,
            ResponseWriter = EscreverVida
        }).AllowAnonymous().ExcludeFromDescription();

        app.MapHealthChecks("/health/pronto", new HealthCheckOptions
        {
            Predicate = registro => registro.Tags.Contains(ExtensoesInfraestrutura.TagPronto),
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
            },
            ResponseWriter = EscreverProntidao
        }).AllowAnonymous().ExcludeFromDescription();

        return app;
    }

    private static Task EscreverVida(HttpContext contexto, HealthReport relatorio) =>
        contexto.Response.WriteAsJsonAsync(new RespostaVida(relatorio.Status.ToString()), contexto.RequestAborted);

    private static Task EscreverProntidao(HttpContext contexto, HealthReport relatorio) =>
        contexto.Response.WriteAsJsonAsync(
            new RespostaProntidao(
                relatorio.Status.ToString(),
                relatorio.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString())),
            contexto.RequestAborted);

    private sealed record RespostaVida(string Status);

    private sealed record RespostaProntidao(string Status, Dictionary<string, string> Dependencias);
}
