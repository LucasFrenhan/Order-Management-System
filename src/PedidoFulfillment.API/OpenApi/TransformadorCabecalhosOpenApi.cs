using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PedidoFulfillment.API.OpenApi;

/// <summary>
/// Documenta o cabeçalho <c>Location</c> nas respostas 201 dos endpoints marcados com
/// <see cref="MetadadosCabecalhoLocation"/>.
/// </summary>
public sealed class TransformadorCabecalhosOpenApi : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var temLocation = context.Description.ActionDescriptor.EndpointMetadata.OfType<MetadadosCabecalhoLocation>().Any();
        if (temLocation && operation.Responses?.TryGetValue("201", out var criado) == true && criado is OpenApiResponse resposta)
        {
            resposta.Headers ??= new Dictionary<string, IOpenApiHeader>();
            resposta.Headers["Location"] = new OpenApiHeader
            {
                Description = "URL do recurso criado.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri-reference" }
            };
        }

        return Task.CompletedTask;
    }
}
