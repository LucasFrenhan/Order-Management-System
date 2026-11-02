using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>Uma requisição recebida pelo receptor de teste.</summary>
public sealed record RequisicaoWebhook(IReadOnlyDictionary<string, string> Cabecalhos, string Corpo, DateTimeOffset RecebidaEm);

/// <summary>
/// Receptor de webhooks de verdade, em Kestrel no loopback com porta livre (REQ-18, T023): grava
/// cabeçalhos e corpo de cada requisição e responde conforme o <see cref="Roteiro"/>, que recebe o
/// número da requisição (a primeira é 1).
/// </summary>
public sealed class ReceptorWebhookTeste : IAsyncDisposable
{
    private readonly ConcurrentQueue<RequisicaoWebhook> recebidas = new();
    private readonly WebApplication app;
    private int contador;

    private ReceptorWebhookTeste(WebApplication app) => this.app = app;

    /// <summary>Status e atraso de cada resposta; o padrão é 204 na hora.</summary>
    public Func<int, (int Status, TimeSpan Atraso)> Roteiro { get; set; } = _ => (StatusCodes.Status204NoContent, TimeSpan.Zero);

    public IReadOnlyCollection<RequisicaoWebhook> Recebidas => [.. recebidas];

    public string Endereco { get; private set; } = "";

    /// <summary>URL de um caminho deste receptor (todos os caminhos respondem igual).</summary>
    public string Url(string caminho = "/oms/webhooks") => Endereco + caminho;

    public static async Task<ReceptorWebhookTeste> IniciarAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        var receptor = new ReceptorWebhookTeste(app);
        app.Run(receptor.ResponderAsync);
        await app.StartAsync(TestContext.Current.CancellationToken);
        receptor.Endereco = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return receptor;
    }

    private async Task ResponderAsync(HttpContext contexto)
    {
        using var leitor = new StreamReader(contexto.Request.Body);
        var corpo = await leitor.ReadToEndAsync(contexto.RequestAborted);
        recebidas.Enqueue(new RequisicaoWebhook(
            contexto.Request.Headers.ToDictionary(c => c.Key.ToLowerInvariant(), c => c.Value.ToString()),
            corpo,
            DateTimeOffset.UtcNow));
        var (status, atraso) = Roteiro(Interlocked.Increment(ref contador));
        if (atraso > TimeSpan.Zero)
        {
            await Task.Delay(atraso, contexto.RequestAborted).ContinueWith(_ => { }, TaskScheduler.Default);
        }

        contexto.Response.StatusCode = status;
        if (status is >= 300 and < 400)
        {
            contexto.Response.Headers.Location = Url("/redirecionado");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
