using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PedidoFulfillment.API.OpenApi;

/// <summary>
/// Ajusta formatos que o gerador nativo não infere: <c>[DataType(Password)]</c> vira
/// <c>format: password</c> e <c>decimal</c> vira <c>format: decimal</c>, como no contrato.
/// </summary>
public sealed class TransformadorFormatosOpenApi : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var atributos = context.JsonPropertyInfo?.AttributeProvider?.GetCustomAttributes(typeof(DataTypeAttribute), true)
            ?? [];
        if (atributos.OfType<DataTypeAttribute>().Any(a => a.DataType == DataType.Password))
        {
            schema.Format = "password";
        }

        var tipo = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (tipo == typeof(decimal))
        {
            schema.Format = "decimal";
        }

        return Task.CompletedTask;
    }
}
