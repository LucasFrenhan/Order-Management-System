using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using PedidoFulfillment.API.Endpoints;
using PedidoFulfillment.API.Erros;
using PedidoFulfillment.API.OpenApi;
using PedidoFulfillment.API.Provisorio;
using PedidoFulfillment.API.Seguranca;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Pedidos;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JsonOptions>(opcoes =>
{
    opcoes.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    opcoes.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip;
    // Números só como número: o padrão web aceita texto e publicaria type [integer, string].
    opcoes.SerializerOptions.NumberHandling = JsonNumberHandling.Strict;
});
// R-08: sem isto, fora de Development, falhas de binding viram 400 sem corpo.
builder.Services.Configure<RouteHandlerOptions>(opcoes => opcoes.ThrowOnBadRequest = true);

builder.Services.AdicionarProblemDetails();
builder.Services.AdicionarAutenticacaoJwt();
builder.Services.AdicionarPoliticasAutorizacao();
builder.Services.AddOpenApi("v1", opcoes =>
{
    opcoes.AddDocumentTransformer<TransformadorSchemasErroOpenApi>();
    opcoes.AddDocumentTransformer<TransformadorSegurancaOpenApi>();
    opcoes.AddOperationTransformer<TransformadorSchemasErroOpenApi>();
    opcoes.AddOperationTransformer<TransformadorSegurancaOpenApi>();
    opcoes.AddOperationTransformer<TransformadorCabecalhosOpenApi>();
    opcoes.AddSchemaTransformer<TransformadorFormatosOpenApi>();
});

builder.Services.AddSingleton<ServicoNaoImplementado>();
builder.Services.AddSingleton<IServicoAutenticacao>(sp => sp.GetRequiredService<ServicoNaoImplementado>());
builder.Services.AddSingleton<IServicoPedidos>(sp => sp.GetRequiredService<ServicoNaoImplementado>());

var app = builder.Build();

app.UsarProblemDetails();
app.UseAuthentication();
app.UseAuthorization();

// Constituição v1.1.0, Princípio III: documentação pública somente fora de produção.
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testes"))
{
    app.MapearEndpointsDocumentacao();
}

app.MapearEndpointsAutenticacao();
app.MapearEndpointsPedidos();

app.Run();

public partial class Program;
