using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Opções de serialização efetivamente configuradas na API.
/// </summary>
public static class OpcoesJson
{
    public static JsonSerializerOptions DaApi(FabricaApi fabrica) =>
        fabrica.Services.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
}
