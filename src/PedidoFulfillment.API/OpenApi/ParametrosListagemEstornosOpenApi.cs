using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PedidoFulfillment.Application.Contratos.Estornos;

namespace PedidoFulfillment.API.OpenApi;

/// <summary>
/// Os parâmetros de <c>GET /api/v1/estornos/pendentes</c> chegam como texto (REQ-28); aqui o documento
/// gerado ganha os tipos do contrato, como nas demais listagens.
/// </summary>
public static class ParametrosListagemEstornosOpenApi
{
    public static Task AplicarAsync(OpenApiOperation operacao, OpenApiOperationTransformerContext contexto, CancellationToken cancellationToken)
    {
        operacao.Parameters =
        [
            new OpenApiParameter
            {
                Name = "tamanho",
                In = ParameterLocation.Query,
                Description = $"Estornos por página, de 1 a {ListarEstornosRequestDTO.TamanhoMaximo}.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32",
                    Default = JsonValue.Create(ListarEstornosRequestDTO.TamanhoPadrao),
                    Minimum = "1",
                    Maximum = ListarEstornosRequestDTO.TamanhoMaximo.ToString(CultureInfo.InvariantCulture)
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
}
