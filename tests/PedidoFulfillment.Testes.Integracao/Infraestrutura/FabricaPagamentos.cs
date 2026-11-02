using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.PagamentosSimulados;
using PedidoFulfillment.PagamentosSimulados.Simulacao;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// A API de pagamento simulada real, em Kestrel no loopback com porta livre (REQ-19, research
/// R-13): o Worker a chama pela rede, como no ambiente local. Modo determinístico (só aprova, sem
/// latência), <c>SemRespostaMs</c> curto e um cliente da rota de controle para os roteiros e para
/// conferir as efetivações (SC-002).
/// </summary>
public sealed class FabricaPagamentos : WebApplicationFactory<ProgramaPagamentos>
{
    public const string ChaveApiTeste = "chave-api-pagamentos-de-teste";
    public const string ChaveControleTeste = "chave-controle-pagamentos-de-teste";

    private HttpClient? controle;

    /// <summary>Valores aplicados depois dos padrões de teste.</summary>
    public IReadOnlyDictionary<string, string?> Configuracoes { get; init; } = new Dictionary<string, string?>();

    public string ChaveApi => ChaveApiTeste;

    /// <summary>O endereço em que a API está ouvindo, com a barra final.</summary>
    public string UrlBase { get; private set; } = "";

    /// <summary>Sobe a API em Kestrel e devolve a fábrica com o <see cref="UrlBase"/> preenchido.</summary>
    public static FabricaPagamentos Iniciar(IReadOnlyDictionary<string, string?>? configuracoes = null)
    {
        var fabrica = new FabricaPagamentos { Configuracoes = configuracoes ?? new Dictionary<string, string?>() };
        // Porta livre no loopback, escolhida pelo próprio Kestrel. UseKestrel(0) fazia a fábrica
        // reescrever os endereços depois da partida, o que às vezes falhava com "Addresses cannot
        // be modified after the server has started" (CI do REQ-20).
        fabrica.UseKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        fabrica.StartServer();
        var endereco = fabrica.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        fabrica.UrlBase = endereco.EndsWith('/') ? endereco : endereco + "/";
        return fabrica;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting("Seguranca:ChaveApi", ChaveApiTeste);
        builder.UseSetting("Seguranca:ChaveControle", ChaveControleTeste);
        builder.UseSetting("Simulacao:Deterministico", "true");
        builder.UseSetting("Simulacao:SemRespostaMs", "2000");
        foreach (var (chave, valor) in Configuracoes)
        {
            builder.UseSetting(chave, valor);
        }

        builder.ConfigureServices(servicos => servicos.AddSingleton<IStartupFilter>(new CapturaTraceparent(TraceparentsRecebidos)));
    }

    /// <summary>O <c>traceparent</c> de cada requisição recebida, para conferir a propagação (FR-020).</summary>
    public ConcurrentQueue<string> TraceparentsRecebidos { get; } = new();

    private sealed class CapturaTraceparent(ConcurrentQueue<string> destino) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> proximo) => app =>
        {
            app.Use(async (contexto, seguinte) =>
            {
                if (contexto.Request.Headers.TryGetValue("traceparent", out var valor))
                {
                    destino.Enqueue(valor.ToString());
                }

                await seguinte(contexto);
            });
            proximo(app);
        };
    }

    /// <summary>Cliente HTTP real da API, sem credencial: cada teste põe a que quer.</summary>
    public HttpClient ClienteHttp() => new() { BaseAddress = new Uri(UrlBase) };

    /// <summary>Cliente com a chave do OMS (<c>X-Api-Key</c>).</summary>
    public HttpClient ClienteApi()
    {
        var cliente = ClienteHttp();
        cliente.DefaultRequestHeaders.Add("X-Api-Key", ChaveApiTeste);
        return cliente;
    }

    private HttpClient Controle
    {
        get
        {
            if (controle is null)
            {
                controle = ClienteHttp();
                controle.DefaultRequestHeaders.Add("X-Chave-Controle", ChaveControleTeste);
            }

            return controle;
        }
    }

    /// <summary>Acrescenta comportamentos ao roteiro, um por chamada (contrato › Controle).</summary>
    public async Task RoteiroAsync(params string[] comportamentos) =>
        (await Controle.PostAsJsonAsync("controle/roteiro", new { comportamentos }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    public async Task ModoAsync(string modo) =>
        (await Controle.PutAsJsonAsync("controle/modo", new { modo }, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    public async Task DesfazerModoAsync() =>
        (await Controle.DeleteAsync("controle/modo", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    public async Task AlterarComportamentoAsync(object parametros) =>
        (await Controle.PutAsJsonAsync("controle/comportamento", parametros, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    public async Task EsvaziarRoteiroAsync() =>
        (await Controle.DeleteAsync("controle/roteiro", TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    /// <summary>Chamadas recebidas desde a partida ou o último restaurar (SC-004).</summary>
    public async Task<long> ChamadasRecebidasAsync() =>
        (await Controle.GetFromJsonAsync<RespostaComportamento>("controle/comportamento", TestContext.Current.CancellationToken))!.ChamadasRecebidas;

    public async Task RestaurarAsync() =>
        (await Controle.PostAsync("controle/restaurar", null, TestContext.Current.CancellationToken)).EnsureSuccessStatusCode();

    /// <summary>Situação, efetivações e chamadas de uma operação; nulo se a API não a tem.</summary>
    public async Task<RespostaOperacaoControle?> OperacaoAsync(string tipo, Guid chave)
    {
        var resposta = await Controle.GetAsync($"controle/operacoes/{tipo}/{chave}", TestContext.Current.CancellationToken);
        if (resposta.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadFromJsonAsync<RespostaOperacaoControle>(TestContext.Current.CancellationToken);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            controle?.Dispose();
        }

        base.Dispose(disposing);
    }
}
