using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Emails;
using PedidoFulfillment.Infrastructure;
using PedidoFulfillment.Infrastructure.Identidade;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>Um relógio que os testes movem: tokens e solicitações usam o <see cref="TimeProvider"/> injetado.</summary>
public sealed class RelogioAjustavel : TimeProvider
{
    public DateTimeOffset Agora { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Agora;
}

/// <summary>Guarda as mensagens e devolve os resultados pedidos pelo teste; sem pedido, envia (REQ-24, R-11).</summary>
public sealed class EnviadorEmailFalso : IEnviadorEmail
{
    private readonly Queue<Func<ResultadoEnvioEmail>> resultados = new();

    public List<MensagemEmail> Mensagens { get; } = [];

    public int Tentativas { get; private set; }

    public void Proximo(ResultadoEnvioEmail resultado) => resultados.Enqueue(() => resultado);

    /// <summary>O e-mail sai, e o processo cai antes de apagar a solicitação (FR-021).</summary>
    public void QuedaDepoisDeEnviar() => resultados.Enqueue(() => throw new InvalidOperationException("Queda simulada."));

    public Task<ResultadoEnvioEmail> EnviarAsync(MensagemEmail mensagem, CancellationToken cancellationToken)
    {
        Tentativas++;
        var resultado = resultados.TryDequeue(out var proximo) ? null : (ResultadoEnvioEmail?)ResultadoEnvioEmail.Enviado;
        if (proximo is not null)
        {
            // Falha de envio não entrega; a queda acontece depois de o e-mail sair.
            try
            {
                resultado = proximo();
            }
            catch (InvalidOperationException)
            {
                Mensagens.Add(mensagem);
                throw;
            }
        }

        if (resultado == ResultadoEnvioEmail.Enviado)
        {
            Mensagens.Add(mensagem);
        }

        return Task.FromResult(resultado!.Value);
    }
}

/// <summary>
/// As portas de identidade, sessões e e-mail sobre um banco real, sem subir a API nem o Worker
/// (REQ-24, R-11): para chamar o armazenamento e o processador direto.
/// </summary>
public sealed class CenarioIdentidade : IAsyncDisposable
{
    public const string Senha = "SenhaForte1";

    private CenarioIdentidade(ServiceProvider servicos, string stringConexao, RelogioAjustavel relogio, EnviadorEmailFalso enviador, CapturaLogs logs)
    {
        Servicos = servicos;
        StringConexao = stringConexao;
        Relogio = relogio;
        Enviador = enviador;
        Logs = logs;
    }

    public ServiceProvider Servicos { get; }

    public string StringConexao { get; }

    public RelogioAjustavel Relogio { get; }

    public EnviadorEmailFalso Enviador { get; }

    public CapturaLogs Logs { get; }

    public static async Task<CenarioIdentidade> CriarAsync(BancoDadosTeste banco)
    {
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SqlServer"] = stringConexao,
            ["RabbitMQ:Servidor"] = FabricaApi.ServidorRabbitMQTeste,
            ["RabbitMQ:Porta"] = "1",
            ["RabbitMQ:Usuario"] = "teste",
            ["RabbitMQ:Senha"] = "senha-de-teste",
            ["RabbitMQ:VirtualHost"] = "/"
        }).Build();
        var relogio = new RelogioAjustavel();
        var enviador = new EnviadorEmailFalso();
        var logs = new CapturaLogs();
        var colecao = new ServiceCollection();
        colecao.AddSingleton<IConfiguration>(configuracao);
        colecao.AddLogging(l => l.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        colecao.AdicionarInfraestrutura(configuracao);
        colecao.Replace(ServiceDescriptor.Singleton<TimeProvider>(relogio));
        colecao.AddSingleton<IEnviadorEmail>(enviador);
        return new CenarioIdentidade(colecao.BuildServiceProvider(), stringConexao, relogio, enviador, logs);
    }

    public IServiceScopeFactory Escopos => Servicos.GetRequiredService<IServiceScopeFactory>();

    public async Task<Guid> ContaAsync(string email = "maria@exemplo.com.br", bool confirmada = false)
    {
        await using var escopo = Servicos.CreateAsyncScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var usuario = new Usuario { UserName = email, Email = email, Nome = "Maria Silva", EmailConfirmed = confirmada };
        var resultado = await usuarios.CreateAsync(usuario, Senha);
        return resultado.Succeeded ? usuario.Id : throw new InvalidOperationException("Conta não criada.");
    }

    public async Task<T> NoContextoAsync<T>(Func<ContextoBancoDados, Task<T>> consulta)
    {
        await using var escopo = Servicos.CreateAsyncScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>());
    }

    public async ValueTask DisposeAsync() => await Servicos.DisposeAsync();
}
