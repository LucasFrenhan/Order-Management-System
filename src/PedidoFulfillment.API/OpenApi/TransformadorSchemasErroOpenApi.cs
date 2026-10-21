using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PedidoFulfillment.API.OpenApi;

/// <summary>
/// Registra os schemas de erro RFC 7807 exatamente como no contrato e adiciona as respostas de
/// erro declaradas por cada endpoint, além do 500 em todas as operações (FR-004, FR-009).
/// </summary>
public sealed class TransformadorSchemasErroOpenApi : IOpenApiDocumentTransformer, IOpenApiOperationTransformer
{
    public const string SchemaProblemDetails = "ProblemDetails";
    public const string SchemaValidacao = "ValidacaoProblemDetails";
    public const string SchemaNegocio = "NegocioProblemDetails";

    private const string TipoProblema = "application/problem+json";

    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Info = new OpenApiInfo
        {
            Title = "Order Management System - API de Pedidos",
            Version = "v1",
            Description = "Contrato público v1 da API do OMS (REQ-01)."
        };
        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Autenticacao", Description = "Registro e login de usuários." },
            new() { Name = "Pedidos", Description = "Criação, consulta e atualização de status de pedidos." }
        };

        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
        document.Components.Schemas[SchemaProblemDetails] = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string> { "type", "title", "status", "instance", "traceId" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["type"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri-reference" },
                ["title"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["status"] = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" },
                ["detail"] = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null },
                ["instance"] = new OpenApiSchema { Type = JsonSchemaType.String },
                ["traceId"] = new OpenApiSchema { Type = JsonSchemaType.String }
            }
        };
        document.Components.Schemas[SchemaValidacao] = Estende(document, "erros", new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            AdditionalProperties = new OpenApiSchema
            {
                Type = JsonSchemaType.Array,
                Items = new OpenApiSchema { Type = JsonSchemaType.String }
            }
        });
        document.Components.Schemas[SchemaNegocio] = Estende(document, "codigo", new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Description = "Código estável do erro."
        });
        return Task.CompletedTask;
    }

    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        operation.Responses ??= new OpenApiResponses();
        var declarados = context.Description.ActionDescriptor.EndpointMetadata.OfType<MetadadosRespostasErro>()
            .SelectMany(m => m.Codigos);

        foreach (var codigo in declarados)
        {
            var (descricao, schema) = codigo switch
            {
                400 => ("Dados de entrada inválidos.", SchemaValidacao),
                401 => ("Não autenticado.", SchemaProblemDetails),
                403 => ("Usuário autenticado sem permissão para a operação.", SchemaProblemDetails),
                404 => ("Recurso não encontrado (ou não visível para o usuário).", SchemaProblemDetails),
                409 => ("Conflito com o estado atual do recurso.", SchemaNegocio),
                422 => ("Requisição bem formada, mas rejeitada por regra de negócio.", SchemaNegocio),
                _ => ("Erro.", SchemaProblemDetails)
            };
            operation.Responses[codigo.ToString()] = RespostaErro(descricao, schema, context.Document);
        }

        operation.Responses.TryAdd("500", RespostaErro("Erro inesperado. Não expõe detalhes internos.", SchemaProblemDetails, context.Document));
        return Task.CompletedTask;
    }

    public static OpenApiResponse RespostaErro(string descricao, string schema, OpenApiDocument? documento) => new()
    {
        Description = descricao,
        Content = new Dictionary<string, OpenApiMediaType>
        {
            [TipoProblema] = new() { Schema = new OpenApiSchemaReference(schema, documento) }
        }
    };

    private static OpenApiSchema Estende(OpenApiDocument documento, string propriedade, IOpenApiSchema schema) => new()
    {
        AllOf =
        [
            new OpenApiSchemaReference(SchemaProblemDetails, documento),
            new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Required = new HashSet<string> { propriedade },
                Properties = new Dictionary<string, IOpenApiSchema> { [propriedade] = schema }
            }
        ]
    };
}
