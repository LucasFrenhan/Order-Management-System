using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Reconciliacao;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Infrastructure.Notificacoes;
using PedidoFulfillment.Infrastructure.Observabilidade;
using PedidoFulfillment.Infrastructure.Pagamentos;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Infrastructure.Reconciliacao;

namespace PedidoFulfillment.Testes.Unitarios.Rotinas;

/// <summary>
/// As rotinas periódicas do Worker com o banco ou o broker fora: a rodada registra a falha e não
/// derruba o processo; o cancelamento do encerramento segue adiante; parar o serviço encerra o laço
/// (REQ-12, REQ-13, REQ-14, REQ-16 e REQ-18). Sem Docker: as conexões vão para uma porta fechada.
/// </summary>
public sealed class RotinasSemDependenciasTestes : IDisposable
{
    // Porta 1 no loopback: a conexão é recusada na hora, sem esperar o prazo.
    private const string BancoInacessivel = "Server=127.0.0.1,1;Database=oms;User Id=sa;Password=x;Encrypt=False;Connect Timeout=2";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private readonly ServiceProvider servicos = new ServiceCollection()
        .AddDbContext<ContextoBancoDados>(o => o.UseSqlServer(BancoInacessivel))
        .BuildServiceProvider();

    private readonly LogsCapturados logs = new();

    public void Dispose() => servicos.Dispose();

    private IServiceScopeFactory Escopos => servicos.GetRequiredService<IServiceScopeFactory>();

    private static IOptions<OpcoesSqlServer> Sql => Options.Create(new OpcoesSqlServer { StringConexao = BancoInacessivel });

    private static CancellationToken Cancelado => new(canceled: true);

    private ProcessadorNotificacoes Notificacoes() =>
        new(Escopos, Options.Create(new OpcoesNotificacoes()), Sql, TimeProvider.System, logs.Para<ProcessadorNotificacoes>());

    private ProcessadorEstornos Estornos() =>
        new(Escopos, Options.Create(new OpcoesEstornos()), Sql, TimeProvider.System, logs.Para<ProcessadorEstornos>());

    private ReconciliadorPedidos Reconciliador() =>
        new(Escopos, Options.Create(new OpcoesReconciliacao()), Sql, new RegistroFalhasReconciliacao(), TimeProvider.System, logs.Para<ReconciliadorPedidos>());

    private ExecutorReenviosDlq Reenvios() =>
        new(Escopos, null!, Options.Create(new OpcoesReenvioDlq()), Sql, TimeProvider.System, logs.Para<ExecutorReenviosDlq>());

    private LimpezaMensageria Limpeza() =>
        new(Escopos, Options.Create(new OpcoesLimpeza()), Sql, TimeProvider.System, logs.Para<LimpezaMensageria>());

    private AmostradorOutbox Amostrador() =>
        new(Escopos, ConfiguracaoComTelemetria(), Options.Create(new OpcoesObservabilidade()), TimeProvider.System, logs.Para<AmostradorOutbox>());

    private MonitorDlq Monitor(ConexaoRabbitMQ conexao) =>
        new(conexao, Options.Create(new OpcoesReenvioDlq()), new ConfigurationBuilder().Build(), TimeProvider.System, logs.Para<MonitorDlq>());

    private ConexaoRabbitMQ BrokerInacessivel() =>
        new(Options.Create(new OpcoesRabbitMQ { Servidor = "127.0.0.1", Porta = 1, Usuario = "x", Senha = "x", VirtualHost = "/" }), logs.Para<ConexaoRabbitMQ>());

    private static IConfiguration ConfiguracaoComTelemetria() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Observabilidade:Habilitada"] = "true" }).Build();

    public static TheoryData<string> Rotinas => ["notificacoes", "estornos", "reconciliacao", "reenvios", "limpeza", "amostrador"];

    private Func<CancellationToken, Task> Rodada(string rotina) => rotina switch
    {
        "notificacoes" => Notificacoes().ExecutarRodadaAsync,
        "estornos" => t => Estornos().ExecutarRodadaAsync(new OpcoesEstornos(), t),
        "reconciliacao" => t => Reconciliador().ExecutarCicloAsync(new OpcoesReconciliacao(), t),
        "reenvios" => Reenvios().ExecutarRodadaAsync,
        "limpeza" => t => Limpeza().ExecutarRodadaAsync(new OpcoesLimpeza(), t),
        "amostrador" => Amostrador().AmostrarAsync,
        _ => throw new ArgumentOutOfRangeException(nameof(rotina))
    };

    private BackgroundService Servico(string rotina) => rotina switch
    {
        "notificacoes" => Notificacoes(),
        "estornos" => Estornos(),
        "reconciliacao" => Reconciliador(),
        "reenvios" => Reenvios(),
        "limpeza" => Limpeza(),
        "amostrador" => Amostrador(),
        _ => throw new ArgumentOutOfRangeException(nameof(rotina))
    };

    [Theory]
    [MemberData(nameof(Rotinas))]
    public async Task Rodada_com_o_banco_fora_registra_a_falha_sem_lancar(string rotina)
    {
        await Rodada(rotina)(Token);

        logs.Registros.Should().NotBeEmpty("a falha da rodada é registrada");
    }

    [Theory]
    [MemberData(nameof(Rotinas))]
    public async Task Rodada_cancelada_no_encerramento_propaga_o_cancelamento(string rotina)
    {
        var rodada = () => Rodada(rotina)(Cancelado);

        await rodada.Should().ThrowAsync<OperationCanceledException>();
        logs.Registros.Should().BeEmpty("o encerramento não é falha");
    }

    [Theory]
    [MemberData(nameof(Rotinas))]
    public async Task Parar_o_servico_encerra_o_laco_sem_erro(string rotina)
    {
        using var servico = Servico(rotina);

        await servico.StartAsync(Token);
        // No .NET 10 o ExecuteAsync roda em segundo plano: espera a primeira rodada (que falha) terminar.
        for (var i = 0; i < 200 && logs.Registros.Count == 0; i++)
        {
            await Task.Delay(50, Token);
        }

        await servico.StopAsync(Token);

        servico.ExecuteTask!.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task Verificacao_da_dlq_com_o_broker_fora_registra_a_falha_e_cancelada_propaga()
    {
        await using var conexao = BrokerInacessivel();
        var monitor = Monitor(conexao);

        await monitor.VerificarAsync(new OpcoesReenvioDlq(), Token);
        var cancelada = () => monitor.VerificarAsync(new OpcoesReenvioDlq(), Cancelado);

        logs.Registros.Should().Contain(r => r.Nivel >= LogLevel.Warning);
        await cancelada.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Rotinas_desligadas_nao_comecam()
    {
        using var limpeza = new LimpezaMensageria(Escopos, Options.Create(new OpcoesLimpeza { Habilitada = false }), Sql, TimeProvider.System, logs.Para<LimpezaMensageria>());

        await limpeza.StartAsync(Token);
        await limpeza.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Token);

        limpeza.ExecuteTask.IsCompletedSuccessfully.Should().BeTrue("termina sozinha, sem esperar o encerramento");
        logs.Registros.Should().BeEmpty();
    }

    /// <summary>Registros de log em memória, de todas as categorias.</summary>
    private sealed class LogsCapturados
    {
        private readonly List<(LogLevel Nivel, string Texto)> registros = [];

        public IReadOnlyList<(LogLevel Nivel, string Texto)> Registros
        {
            get
            {
                lock (registros)
                {
                    return [.. registros];
                }
            }
        }

        public ILogger<T> Para<T>() => new Registrador<T>(this);

        private void Adicionar(LogLevel nivel, string texto)
        {
            lock (registros)
            {
                registros.Add((nivel, texto));
            }
        }

        private sealed class Registrador<T>(LogsCapturados destino) : ILogger<T>
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    destino.Adicionar(logLevel, formatter(state, exception));
                }
            }
        }
    }
}
