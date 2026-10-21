using System.Globalization;
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

    // Portas fechadas: o caminho de falha da prontidão roda sem Docker (research R-07).
    public const string StringConexaoTeste =
        "Server=127.0.0.1,1;Database=PedidoFulfillment;User Id=sa;Password=senha-de-teste;Encrypt=True;TrustServerCertificate=True;Connect Timeout=2";
    public const string ServidorRabbitMQTeste = "127.0.0.1";
    public const int PortaRabbitMQTeste = 1;

    public string Ambiente { get; init; } = "Testes";

    /// <summary>
    /// Quando verdadeiro (padrão), passa valores de teste para banco e broker, que a API
    /// valida na inicialização.
    /// </summary>
    public bool ComConfiguracaoInfraestrutura { get; init; } = true;

    /// <summary>
    /// Liga a preparação do banco na inicialização (REQ-03). Desligada por padrão, para que a
    /// suíte rode sem banco (research R-11).
    /// </summary>
    public bool PrepararBanco { get; init; }

    /// <summary>String de conexão no lugar da de porta fechada (por exemplo, um banco de teste).</summary>
    public string? StringConexao { get; init; }

    /// <summary>Valores aplicados depois dos padrões, para sobrescrevê-los.</summary>
    public IReadOnlyDictionary<string, string?> Configuracoes { get; init; } = new Dictionary<string, string?>();

    public IServicoAutenticacao? ServicoAutenticacao { get; init; }

    public IServicoPedidos? ServicoPedidos { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Ambiente);
        builder.UseSetting("Jwt:ChaveAssinatura", ChaveTeste);
        builder.UseSetting("Jwt:Emissor", EmissorTeste);
        builder.UseSetting("Jwt:Audiencia", AudienciaTeste);
        builder.UseSetting("Banco:PrepararNaInicializacao", PrepararBanco ? "true" : "false");
        if (ComConfiguracaoInfraestrutura)
        {
            builder.UseSetting("ConnectionStrings:SqlServer", StringConexao ?? StringConexaoTeste);
            builder.UseSetting("RabbitMQ:Servidor", ServidorRabbitMQTeste);
            builder.UseSetting("RabbitMQ:Porta", PortaRabbitMQTeste.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RabbitMQ:Usuario", "teste");
            builder.UseSetting("RabbitMQ:Senha", "senha-de-teste");
            builder.UseSetting("RabbitMQ:VirtualHost", "/");
        }

        foreach (var (chave, valor) in Configuracoes)
        {
            builder.UseSetting(chave, valor);
        }

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
