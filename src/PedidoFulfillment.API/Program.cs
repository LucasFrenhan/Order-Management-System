using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PedidoFulfillment.API.Endpoints;
using PedidoFulfillment.API.Erros;
using PedidoFulfillment.API.Observabilidade;
using PedidoFulfillment.API.OpenApi;
using PedidoFulfillment.API.Seguranca;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Notificacoes;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Application.Produtos;
using PedidoFulfillment.Infrastructure;
using PedidoFulfillment.Infrastructure.Notificacoes;
using PedidoFulfillment.Infrastructure.Observabilidade;
using PedidoFulfillment.Infrastructure.Saude;

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
builder.Services.AdicionarInfraestrutura(builder.Configuration);
// Destinos de webhook e eventos em tempo real (REQ-18).
builder.Services.AdicionarNotificacoes();
builder.Services.AdicionarEventosTempoReal();
// Traces, métricas e logs por OTLP, só com destino configurado (REQ-16).
builder.AdicionarObservabilidade("oms-api");
builder.Services.AddOpenApi("v1", opcoes =>
{
    opcoes.AddDocumentTransformer<TransformadorSchemasErroOpenApi>();
    opcoes.AddDocumentTransformer<TransformadorSegurancaOpenApi>();
    opcoes.AddOperationTransformer<TransformadorSchemasErroOpenApi>();
    opcoes.AddOperationTransformer<TransformadorSegurancaOpenApi>();
    opcoes.AddOperationTransformer<TransformadorCabecalhosOpenApi>();
    opcoes.AddSchemaTransformer<TransformadorFormatosOpenApi>();
});

builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IEmissorToken, EmissorTokenJwt>();
builder.Services.AddScoped<IServicoAutenticacao, ServicoAutenticacao>();
builder.Services.AddScoped<IServicoPedidos, ServicoPedidos>();
// Chave do cursor da listagem (REQ-21, research R-04): hoje derivada da chave do JWT. Para usar
// uma chave própria do cursor, troque só esta linha por outra IChaveCursor.
builder.Services.AddSingleton<IChaveCursor, ChaveCursorDerivadaDoJwt>();
builder.Services.AddSingleton<CodificadorCursorPedidos>();
builder.Services.AddScoped<IServicoListagemPedidos, ServicoListagemPedidos>();
builder.Services.AddSingleton<CodificadorCursorProdutos>();
builder.Services.AddScoped<IServicoCatalogo, ServicoCatalogo>();
builder.Services.AddScoped<IServicoReenvioDlq, ServicoReenvioDlq>();
builder.Services.AddScoped<IServicoDestinosNotificacao, ServicoDestinosNotificacao>();

var app = builder.Build();

app.UsarProblemDetails();
app.UseAuthentication();
// Usuário e pedido no span da requisição (REQ-16, FR-001).
app.UsarAtributosRequisicao();
app.UseAuthorization();

// Constituição v1.1.0, Princípio III: documentação pública somente fora de produção.
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testes"))
{
    app.MapearEndpointsDocumentacao();
}

app.MapearEndpointsAutenticacao();
app.MapearEndpointsPedidos();
app.MapearEndpointsProdutos();
app.MapearEndpointsAdministracao();
app.MapearEndpointsNotificacoes();
// Saúde existe em todos os ambientes (REQ-02); pública e fora do OpenAPI.
app.MapearEndpointsSaude();

app.Run();

public partial class Program;
