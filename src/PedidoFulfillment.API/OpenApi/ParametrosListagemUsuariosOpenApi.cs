using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using PedidoFulfillment.Application.Contratos.Usuarios;

namespace PedidoFulfillment.API.OpenApi;

/// <summary>
/// Os parâmetros de <c>GET /api/v1/admin/usuarios</c> chegam como texto (REQ-25); aqui o documento
/// gerado ganha os tipos do contrato, como nas demais listagens.
/// </summary>
public static class ParametrosListagemUsuariosOpenApi
{
    public static Task AplicarAsync(OpenApiOperation operacao, OpenApiOperationTransformerContext contexto, CancellationToken cancellationToken)
    {
        operacao.Parameters =
        [
            new OpenApiParameter
            {
                Name = "perfil",
                In = ParameterLocation.Query,
                Description = "Só as contas com este perfil.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = [JsonValue.Create("Cliente"), JsonValue.Create("Admin"), JsonValue.Create("OperadorLogistica")]
                }
            },
            new OpenApiParameter
            {
                Name = "situacao",
                In = ParameterLocation.Query,
                Description = "Só as contas nesta situação.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = [JsonValue.Create("Ativa"), JsonValue.Create("Desativada")]
                }
            },
            new OpenApiParameter
            {
                Name = "busca",
                In = ParameterLocation.Query,
                Description = "Trecho do nome ou do e-mail, sem diferenciar maiúsculas nem acentos (de 2 a 100 caracteres).",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            },
            new OpenApiParameter
            {
                Name = "tamanho",
                In = ParameterLocation.Query,
                Description = $"Usuários por página, de 1 a {ListarUsuariosRequestDTO.TamanhoMaximo}.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Format = "int32",
                    Default = JsonValue.Create(ListarUsuariosRequestDTO.TamanhoPadrao),
                    Minimum = "1",
                    Maximum = ListarUsuariosRequestDTO.TamanhoMaximo.ToString(CultureInfo.InvariantCulture)
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
