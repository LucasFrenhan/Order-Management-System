using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PedidoFulfillment.API.OpenApi;

/// <summary>
/// Declara o esquema <c>bearerAuth</c> e a exigência de token por operação (FR-006): operações
/// anônimas recebem <c>security: []</c>; as protegidas, <c>bearerAuth</c> e as respostas 401/403.
/// </summary>
public sealed class TransformadorSegurancaOpenApi : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public const string EsquemaBearer = "bearerAuth";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes[EsquemaBearer] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Token emitido por POST /api/v1/auth/login."
        };
        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        operation.Security = [];
        if (EhAnonima(context))
        {
            return Task.CompletedTask;
        }

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(EsquemaBearer, context.Document)] = []
        });
        operation.Responses ??= new OpenApiResponses();
        operation.Responses.TryAdd("401", TransformadorSchemasErroOpenApi.RespostaErro(
            "Token ausente, inválido ou expirado.", TransformadorSchemasErroOpenApi.SchemaProblemDetails, context.Document));
        operation.Responses.TryAdd("403", TransformadorSchemasErroOpenApi.RespostaErro(
            "Usuário autenticado sem permissão para a operação.", TransformadorSchemasErroOpenApi.SchemaProblemDetails, context.Document));
        return Task.CompletedTask;
    }

    private static bool EhAnonima(OpenApiOperationTransformerContext context) =>
        context.Description.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any();
}
