using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.LimiteRequisicoes;
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

    /// <summary>Chave AES-GCM dos segredos dos webhooks nos testes (REQ-18), igual na API e no Worker.</summary>
    public const string ChaveCifraTeste = "Y2hhdmUtZGUtdGVzdGUtYXV0b21hdGl6YWRvLTMyYnk=";
    /// <summary>Chave das contagens do limite de requisições nos testes (REQ-23), igual em todas as instâncias.</summary>
    public const string ChaveContagemTeste = "Y2hhdmUtZGUtdGVzdGUtYXV0b21hdGl6YWRvLTMyYnk=";

    /// <summary>O teto da validação: os testes que não tratam do limite nunca o atingem (REQ-23, R-10).</summary>
    public const string LimiteAltoTeste = "1000000";

    /// <summary>As políticas das rotas do ciclo de autenticação (REQ-24, FR-024).</summary>
    private static readonly string[] PoliticasCicloAutenticacao =
    [
        "RecuperacaoOrigemEmail", "RecuperacaoOrigem", "ReenvioOrigemEmail", "ReenvioOrigem", "Redefinicao", "Confirmacao", "Renovacao", "Logout"
    ];

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

    /// <summary>Liga a telemetria (REQ-16) e coleta spans, logs e métricas em memória.</summary>
    public ColetorTelemetria? Telemetria { get; init; }

    /// <summary>Quando informado, recebe todos os logs da API.</summary>
    public CapturaLogs? Logs { get; init; }

    /// <summary>Recebe a exceção original de uma falha de partida (ver <see cref="Infraestrutura.FalhaPartida"/>).</summary>
    public FalhaPartida FalhaPartida { get; } = new();

    public IServicoAutenticacao? ServicoAutenticacao { get; init; }

    public IServicoPedidos? ServicoPedidos { get; init; }

    /// <summary>
    /// O estado dos limites (REQ-23). Sem informar: o armazenamento real, com <see cref="StringConexao"/>,
    /// ou um em memória, para a suíte rodar sem banco (R-10).
    /// </summary>
    public IArmazenamentoContagens? ArmazenamentoContagens { get; init; }

    /// <summary>O endereço da conexão: o <c>TestServer</c> não informa nenhum (REQ-23, R-10).</summary>
    public IPAddress? EnderecoConexao { get; init; }

    /// <summary>Outro relógio, como o de uma instância com a hora errada (REQ-23, FR-008).</summary>
    public TimeProvider? Relogio { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Ambiente);
        // REQ-23 (R-10): limites altos, sem desligar o código; sem a saída por tempo, que num teste
        // de limite exato liberaria uma requisição sem haver bug.
        builder.UseSetting("LimiteRequisicoes:ChaveContagem", ChaveContagemTeste);
        builder.UseSetting("LimiteRequisicoes:LoginOrigemEmail:Limite", LimiteAltoTeste);
        builder.UseSetting("LimiteRequisicoes:LoginOrigem:Limite", LimiteAltoTeste);
        builder.UseSetting("LimiteRequisicoes:Registro:Limite", LimiteAltoTeste);
        foreach (var politica in PoliticasCicloAutenticacao)
        {
            builder.UseSetting($"LimiteRequisicoes:{politica}:Limite", LimiteAltoTeste);
        }

        builder.UseSetting("LimiteRequisicoes:TempoMaximoEsperaMs", "5000");
        builder.UseSetting("LimiteRequisicoes:PausaAposFalhaSegundos", "0");
        builder.UseSetting("Jwt:ChaveAssinatura", ChaveTeste);
        builder.UseSetting("Jwt:Emissor", EmissorTeste);
        builder.UseSetting("Jwt:Audiencia", AudienciaTeste);
        builder.UseSetting("Banco:PrepararNaInicializacao", PrepararBanco ? "true" : "false");
        // REQ-18: a mesma chave do Worker; a rede local só onde o validador a aceita.
        builder.UseSetting("Notificacoes:ChaveCifra", ChaveCifraTeste);
        builder.UseSetting("Notificacoes:PermitirRedeLocal", Ambiente is "Testes" or "Development" ? "true" : "false");
        if (ComConfiguracaoInfraestrutura)
        {
            builder.UseSetting("ConnectionStrings:SqlServer", StringConexao ?? StringConexaoTeste);
            builder.UseSetting("RabbitMQ:Servidor", ServidorRabbitMQTeste);
            builder.UseSetting("RabbitMQ:Porta", PortaRabbitMQTeste.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("RabbitMQ:Usuario", "teste");
            builder.UseSetting("RabbitMQ:Senha", "senha-de-teste");
            builder.UseSetting("RabbitMQ:VirtualHost", "/");
        }

        if (Telemetria is not null)
        {
            builder.UseSetting("Observabilidade:Habilitada", "true");
        }

        foreach (var (chave, valor) in Configuracoes)
        {
            builder.UseSetting(chave, valor);
        }

        if (Logs is not null)
        {
            builder.ConfigureLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Trace));
        }

        builder.ConfigureLogging(logging => logging.AddProvider(FalhaPartida));

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IStartupFilter, FiltroRotaExcecao>();
            if (EnderecoConexao is not null)
            {
                services.AddSingleton<IStartupFilter>(new FiltroEnderecoConexao(EnderecoConexao));
            }

            if (ArmazenamentoContagens is not null)
            {
                services.Replace(ServiceDescriptor.Singleton(ArmazenamentoContagens));
            }
            else if (StringConexao is null)
            {
                services.Replace(ServiceDescriptor.Singleton<IArmazenamentoContagens>(new ArmazenamentoContagensMemoria()));
            }

            if (Relogio is not null)
            {
                services.Replace(ServiceDescriptor.Singleton(Relogio));
            }

            if (ServicoAutenticacao is not null)
            {
                services.Replace(ServiceDescriptor.Singleton(ServicoAutenticacao));
            }

            if (ServicoPedidos is not null)
            {
                services.Replace(ServiceDescriptor.Singleton(ServicoPedidos));
            }

            Telemetria?.RegistrarEm(services);
        });
    }

    /// <summary>No começo do pipeline, antes dos cabeçalhos encaminhados: o que uma conexão real informaria.</summary>
    private sealed class FiltroEnderecoConexao(IPAddress endereco) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext contexto, RequestDelegate proximo) =>
            {
                contexto.Connection.RemoteIpAddress = endereco;
                return proximo(contexto);
            });
            next(app);
        };
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
