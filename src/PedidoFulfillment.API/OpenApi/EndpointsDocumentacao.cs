using Scalar.AspNetCore;

namespace PedidoFulfillment.API.OpenApi;

/// <summary>
/// Publica a documentação do contrato (research R-03): o YAML aprovado em
/// <c>/openapi/v1.yaml</c> e a interface Scalar em <c>/scalar/v1</c>, que mostra esse YAML.
/// O documento gerado (<c>/openapi/v1.json</c>) serve ao teste de conformidade.
/// </summary>
public static class EndpointsDocumentacao
{
    private const string RotaContrato = "/openapi/{documentName}.yaml";

    public static WebApplication MapearEndpointsDocumentacao(this WebApplication app)
    {
        app.MapOpenApi().AllowAnonymous();

        var caminhoContrato = Path.Combine(AppContext.BaseDirectory, "Contrato", "openapi-v1.yaml");
        app.MapGet("/openapi/v1.yaml", () => TypedResults.PhysicalFile(caminhoContrato, "application/yaml"))
            .ExcludeFromDescription()
            .AllowAnonymous();

        app.MapScalarApiReference(opcoes => opcoes
                .WithTitle("OMS - API v1")
                .WithOpenApiRoutePattern(RotaContrato)
                .AddPreferredSecuritySchemes(TransformadorSegurancaOpenApi.EsquemaBearer))
            .AllowAnonymous();

        return app;
    }
}
