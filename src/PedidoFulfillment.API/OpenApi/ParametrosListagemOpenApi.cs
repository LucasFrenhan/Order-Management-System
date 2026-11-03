using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PedidoFulfillment.Application.Contratos.Pedidos;

namespace PedidoFulfillment.API.OpenApi;

/// <summary>
/// Os parâmetros de <c>GET /api/v1/pedidos</c> chegam como texto (REQ-21, research R-05); aqui o
/// documento gerado ganha os tipos do contrato: <c>date-time</c>, <c>uuid</c>, <c>int32</c> de 1 a
/// 100 e a lista de <c>StatusPedido</c>.
/// </summary>
public static class ParametrosListagemOpenApi
{
    public static Task AplicarAsync(OpenApiOperation operacao, OpenApiOperationTransformerContext contexto, CancellationToken cancellationToken)
    {
        operacao.Parameters =
        [
            new OpenApiParameter
            {
                Name = "status",
                In = ParameterLocation.Query,
                Description = "Um ou mais status; repita o parâmetro para mais de um.",
                Style = ParameterStyle.Form,
                Explode = true,
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Array,
                    Items = new OpenApiSchemaReference("StatusPedido", contexto.Document)
                }
            },
            Data("criadoDe", "Início (inclusivo) do período de criação, com fuso."),
            Data("criadoAte", "Fim (inclusivo) do período de criação, com fuso."),
            Data("atualizadoDe", "Início (inclusivo) do período de última alteração, com fuso."),
            Data("atualizadoAte", "Fim (inclusivo) do período de última alteração, com fuso."),
            new OpenApiParameter
            {
                Name = "clienteId",
                In = ParameterLocation.Query,
                Description = "Somente Admin e OperadorLogistica.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" }
            },
            new OpenApiParameter
            {
                Name = "tamanho",
                In = ParameterLocation.Query,
                Description = $"Pedidos por página, de 1 a {ListarPedidosRequestDTO.TamanhoMaximo}.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32",
                    Default = JsonValue.Create(ListarPedidosRequestDTO.TamanhoPadrao),
                    Minimum = "1",
                    Maximum = ListarPedidosRequestDTO.TamanhoMaximo.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }
            },
            new OpenApiParameter
            {
                Name = "cursor",
                In = ParameterLocation.Query,
                Description = "O `proximo` ou o `anterior` de uma resposta anterior.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            }
        ];

        return Task.CompletedTask;
    }

    private static OpenApiParameter Data(string nome, string descricao) => new()
    {
        Name = nome,
        In = ParameterLocation.Query,
        Description = descricao,
        Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time" }
    };
}
