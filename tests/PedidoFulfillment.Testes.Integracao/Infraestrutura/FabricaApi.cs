using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// API em memória no ambiente <c>Testes</c> (ou outro), com chave JWT de teste e a
/// possibilidade de trocar os serviços de aplicação por fakes.
/// </summary>
public class FabricaApi : WebApplicationFactory<Program>
{
    public const string ChaveTeste = "chave-de-teste-somente-para-testes-automatizados";
    public const string EmissorTeste = "PedidoFulfillment";
    public const string AudienciaTeste = "PedidoFulfillment";
    public const string RotaExcecaoTeste = "/_teste/excecao";

    public string Ambiente { get; init; } = "Testes";

    public IServicoAutenticacao? ServicoAutenticacao { get; init; }

    public IServicoPedidos? ServicoPedidos { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Ambiente);
        builder.UseSetting("Jwt:ChaveAssinatura", ChaveTeste);
        builder.UseSetting("Jwt:Emissor", EmissorTeste);
        builder.UseSetting("Jwt:Audiencia", AudienciaTeste);
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter, FiltroRotaExcecao>();
            if (ServicoAutenticacao is not null)
            {
                services.Replace(ServiceDescriptor.Singleton(ServicoAutenticacao));
            }

            if (ServicoPedidos is not null)
            {
                services.Replace(ServiceDescriptor.Singleton(ServicoPedidos));
            }
        });
    }

    /// <summary>
    /// Middleware no fim do pipeline que lança uma exceção na rota de teste, para provar que
    /// erros inesperados viram 500 sem detalhes internos.
    /// </summary>
    private sealed class FiltroRotaExcecao : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use((HttpContext contexto, RequestDelegate proximo) =>
                contexto.Request.Path == RotaExcecaoTeste
                    ? throw new InvalidOperationException("Falha interna de teste com dado sensível.")
                    : proximo(contexto));
        };
    }
}

public static class ClienteHttpExtensoes
{
    public static HttpClient ComToken(this HttpClient cliente, params PerfilUsuario[] perfis) =>
        cliente.ComToken(GeradorTokenTeste.Gerar(perfis: perfis));

    public static HttpClient ComToken(this HttpClient cliente, string token)
    {
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }
}
