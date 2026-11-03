using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PedidoFulfillment.Application.Contratos.Produtos;

namespace PedidoFulfillment.API.OpenApi;

/// <summary>
/// Os parâmetros de <c>GET /api/v1/produtos</c> chegam como texto (REQ-22, research R-10); aqui o
/// documento gerado ganha os tipos do contrato, como na listagem de pedidos.
/// </summary>
public static class ParametrosListagemProdutosOpenApi
{
    public static Task AplicarAsync(OpenApiOperation operacao, OpenApiOperationTransformerContext contexto, CancellationToken cancellationToken)
    {
        operacao.Parameters =
        [
            new OpenApiParameter
            {
                Name = "busca",
                In = ParameterLocation.Query,
                Description = "Palavras do nome, do SKU ou da descrição, em português (de 2 a 100 caracteres, até 10 palavras).",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            },
            new OpenApiParameter
            {
                Name = "situacao",
                In = ParameterLocation.Query,
                Description = "Somente Admin e OperadorLogistica: Ativos, Inativos ou Todos (padrão).",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = [JsonValue.Create("Ativos"), JsonValue.Create("Inativos"), JsonValue.Create("Todos")]
                }
            },
            new OpenApiParameter
            {
                Name = "tamanho",
                In = ParameterLocation.Query,
                Description = $"Produtos por página, de 1 a {ListarProdutosRequestDTO.TamanhoMaximo}.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32",
                    Default = JsonValue.Create(ListarProdutosRequestDTO.TamanhoPadrao),
                    Minimum = "1",
                    Maximum = ListarProdutosRequestDTO.TamanhoMaximo.ToString(CultureInfo.InvariantCulture)
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
