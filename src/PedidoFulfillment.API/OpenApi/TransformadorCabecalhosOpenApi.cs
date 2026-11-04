using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PedidoFulfillment.API.LimiteRequisicoes;

namespace PedidoFulfillment.API.OpenApi;

/// <summary>
/// Documenta o cabeçalho <c>Location</c> nas respostas 201 dos endpoints marcados com
/// <see cref="MetadadosCabecalhoLocation"/> e o <c>Retry-After</c> no 429 das rotas com
/// <see cref="MetadadosLimiteRequisicoes"/> (REQ-23).
/// </summary>
public sealed class TransformadorCabecalhosOpenApi : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var metadados = context.Description.ActionDescriptor.EndpointMetadata;
        var temLocation = metadados.OfType<MetadadosCabecalhoLocation>().Any();
        if (temLocation && operation.Responses?.TryGetValue("201", out var criado) == true && criado is OpenApiResponse resposta)
        {
            resposta.Headers ??= new Dictionary<string, IOpenApiHeader>();
            resposta.Headers["Location"] = new OpenApiHeader
            {
                Description = "URL do recurso criado.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri-reference" }
            };
        }

        if (metadados.OfType<MetadadosLimiteRequisicoes>().Any()
            && operation.Responses?.TryGetValue("429", out var limitada) == true && limitada is OpenApiResponse respostaLimitada)
        {
            respostaLimitada.Headers ??= new Dictionary<string, IOpenApiHeader>();
            respostaLimitada.Headers["Retry-After"] = new OpenApiHeader
            {
                Description = "Segundos a esperar antes de tentar de novo.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", Minimum = "1" }
            };
        }

        return Task.CompletedTask;
    }
}
