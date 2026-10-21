using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace PedidoFulfillment.API.OpenApi;

/// <summary>
/// Ajusta o que o gerador nativo não infere e o contrato exige: enums como <c>type: string</c>,
/// <c>[EmailAddress]</c> como <c>format: email</c>, <c>[DataType(Password)]</c> como
/// <c>format: password</c> e <c>decimal</c> como <c>format: decimal</c>.
/// </summary>
public sealed class TransformadorFormatosOpenApi : IOpenApiSchemaTransformer
{
    public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken)
    {
        var tipo = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (tipo.IsEnum && schema.Enum is { Count: > 0 })
        {
            schema.Type = JsonSchemaType.String;
        }

        if (tipo == typeof(decimal))
        {
            schema.Format = "decimal";
        }

        var atributos = context.JsonPropertyInfo?.AttributeProvider?.GetCustomAttributes(true) ?? [];
        if (atributos.OfType<EmailAddressAttribute>().Any())
        {
            schema.Format = "email";
        }

        if (atributos.OfType<DataTypeAttribute>().Any(a => a.DataType == DataType.Password))
        {
            schema.Format = "password";
        }

        return Task.CompletedTask;
    }
}
