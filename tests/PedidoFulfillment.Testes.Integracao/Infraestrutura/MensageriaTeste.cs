using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Builders;
using Microsoft.Data.SqlClient;
using PedidoFulfillment.Infrastructure.Mensageria;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

[CollectionDefinition(nameof(ColecaoMensageria))]
public sealed class ColecaoMensageria : ICollectionFixture<MensageriaTeste>;

/// <summary>
/// SQL Server e RabbitMQ reais para os testes de mensageria (REQ-06, T015), com um banco novo
/// por teste. Sem Docker, os testes são ignorados, exceto com OMS_EXIGIR_DOCKER=true (REQ-07).
/// </summary>
public sealed class MensageriaTeste : IAsyncLifetime
{
    private const string UsuarioRabbitMQ = "oms";
    private const string SenhaRabbitMQ = "oms-teste-mensageria";

    /// <summary>Usuário da API no broker, sem permissões: só conecta para a prontidão (REQ-14, R-09).</summary>
    public const string UsuarioRabbitMQApi = "oms-api";

    public const string SenhaRabbitMQApi = "oms-api-teste-mensageria";

    private static readonly TimeSpan PrazoBrokerNoAr = TimeSpan.FromSeconds(60);

    private MsSqlContainer? sqlServer;
    private RabbitMqContainer? rabbitMQ;

    /// <summary>
    /// Porta fixa do host para o AMQP (REQ-17, R-09): parar e iniciar o contêiner não troca a
    /// porta que o Worker já usa.
    /// </summary>
    private int portaAmqp;

    public bool DockerDisponivel { get; private set; }

    public string NovoBanco() =>
        new SqlConnectionStringBuilder(sqlServer!.GetConnectionString()) { InitialCatalog = $"oms_{Guid.NewGuid():N}" }.ConnectionString;

    public async Task<string> NovoBancoMigradoAsync()
    {
        var stringConexao = NovoBanco();
        await BancoDadosTeste.AplicarMigrationsAsync(stringConexao);
        return stringConexao;
    }

    /// <summary>Configuração do broker do contêiner, no formato da seção RabbitMQ.</summary>
    public IReadOnlyDictionary<string, string?> ConfiguracoesBroker => new Dictionary<string, string?>
    {
        ["RabbitMQ:Servidor"] = rabbitMQ!.Hostname,
        ["RabbitMQ:Porta"] = portaAmqp.ToString(CultureInfo.InvariantCulture),
        ["RabbitMQ:Usuario"] = UsuarioRabbitMQ,
        ["RabbitMQ:Senha"] = SenhaRabbitMQ,
        ["RabbitMQ:VirtualHost"] = "/"
    };

    /// <summary>Senha do usuário do Worker no broker, para conferir que ela não vaza (REQ-17).</summary>
    public static string SenhaBroker => SenhaRabbitMQ;

    /// <summary>Credenciais da API no broker (REQ-14, R-09), no formato da seção RabbitMQ.</summary>
    public IReadOnlyDictionary<string, string?> ConfiguracoesBrokerApi => new Dictionary<string, string?>(ConfiguracoesBroker)
    {
        ["RabbitMQ:Usuario"] = UsuarioRabbitMQApi,
        ["RabbitMQ:Senha"] = SenhaRabbitMQApi
    };

    public Task<IConnection> ConectarComoApiAsync() => new ConnectionFactory
    {
        HostName = rabbitMQ!.Hostname,
        Port = portaAmqp,
        UserName = UsuarioRabbitMQApi,
        Password = SenhaRabbitMQApi
    }.CreateConnectionAsync(TestContext.Current.CancellationToken);

    /// <summary>Simula o broker fora do ar sem trocar a porta mapeada.</summary>
    public Task PausarBrokerAsync() => rabbitMQ!.PauseAsync(TestContext.Current.CancellationToken);

    public Task RetomarBrokerAsync() => rabbitMQ!.UnpauseAsync(TestContext.Current.CancellationToken);

    /// <summary>Para o contêiner do broker: as conexões caem como num reinício real (REQ-17).</summary>
    public Task PararBrokerAsync() => rabbitMQ!.StopAsync(TestContext.Current.CancellationToken);

    /// <summary>Inicia de novo o mesmo contêiner (mesmos dados) e espera o AMQP aceitar conexão.</summary>
    public async Task IniciarBrokerAsync()
    {
        await rabbitMQ!.StartAsync(TestContext.Current.CancellationToken);
        await EsperarBrokerNoArAsync();
    }

    /// <summary>Para e inicia o contêiner do broker (REQ-17, R-09), com uma espera opcional parado.</summary>
    public async Task ReiniciarBrokerAsync(TimeSpan? parado = null)
    {
        await PararBrokerAsync();
        if (parado is { } espera)
        {
            await Task.Delay(espera, TestContext.Current.CancellationToken);
        }

        await IniciarBrokerAsync();
    }

    /// <summary>
    /// Apaga tudo do broker (filas, ligações e usuários), como um broker recriado sem dados, e
    /// recria os usuários dos testes (REQ-17).
    /// </summary>
    public async Task RecriarBrokerAsync()
    {
        await ExecutarNoBrokerAsync("rabbitmqctl", "stop_app");
        await ExecutarNoBrokerAsync("rabbitmqctl", "reset");
        await ExecutarNoBrokerAsync("rabbitmqctl", "start_app");
        await ExecutarNoBrokerAsync("rabbitmqctl", "await_startup");
        // Um nó zerado recria o usuário padrão do contêiner; se não recriar, cria aqui.
        var usuarios = await rabbitMQ!.ExecAsync(["rabbitmqctl", "list_users", "--quiet"], TestContext.Current.CancellationToken);
        if (!usuarios.Stdout.Contains(UsuarioRabbitMQ + "\t", StringComparison.Ordinal))
        {
            await ExecutarNoBrokerAsync("rabbitmqctl", "add_user", UsuarioRabbitMQ, SenhaRabbitMQ);
            await ExecutarNoBrokerAsync("rabbitmqctl", "set_user_tags", UsuarioRabbitMQ, "administrator");
            await ExecutarNoBrokerAsync("rabbitmqctl", "set_permissions", "-p", "/", UsuarioRabbitMQ, ".*", ".*", ".*");
        }

        await CriarUsuarioApiAsync();
        await EsperarBrokerNoArAsync();
    }

    /// <summary>Fecha à força todas as conexões abertas no broker (REQ-17).</summary>
    public Task FecharConexoesAsync() => ExecutarNoBrokerAsync("rabbitmqctl", "close_all_connections", "teste REQ-17");

    /// <summary>Troca a senha do usuário do Worker no broker (REQ-17, credenciais recusadas).</summary>
    public Task TrocarSenhaAsync(string senha) => ExecutarNoBrokerAsync("rabbitmqctl", "change_password", UsuarioRabbitMQ, senha);

    /// <summary>Volta a senha original do usuário do Worker.</summary>
    public Task RestaurarSenhaAsync() => TrocarSenhaAsync(SenhaRabbitMQ);

    private async Task ExecutarNoBrokerAsync(params string[] comando)
    {
        var resultado = await rabbitMQ!.ExecAsync(comando, TestContext.Current.CancellationToken);
        if (resultado.ExitCode != 0)
        {
            throw new InvalidOperationException($"Falha em '{string.Join(' ', comando)}' no broker: {resultado.Stderr}");
        }
    }

    /// <summary>
    /// Espera o AMQP aceitar conexão: a espera do Testcontainers na partida lê o log, que depois
    /// de um reinício já tem a linha da partida anterior.
    /// </summary>
    private async Task EsperarBrokerNoArAsync()
    {
        var conectou = await EsperarAteAsync(async () =>
        {
            try
            {
                await using var conexao = await ConectarAsync();
                return true;
            }
            catch (Exception excecao) when (excecao is RabbitMQ.Client.Exceptions.BrokerUnreachableException or IOException)
            {
                return false;
            }
        }, PrazoBrokerNoAr);
        if (!conectou)
        {
            throw new TimeoutException("O broker não voltou a aceitar conexões no prazo.");
        }
    }

    /// <summary>Quantos consumidores a fila tem, ou nulo se ela não existe (REQ-17).</summary>
    public async Task<uint?> ConsumidoresNaFilaAsync(string fila)
    {
        await using var conexao = await ConectarAsync();
        await using var canal = await conexao.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            return (await canal.QueueDeclarePassiveAsync(fila, TestContext.Current.CancellationToken)).ConsumerCount;
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return null;
        }
    }

    public Task<IConnection> ConectarAsync() => new ConnectionFactory
    {
        HostName = rabbitMQ!.Hostname,
        Port = portaAmqp,
        UserName = UsuarioRabbitMQ,
        Password = SenhaRabbitMQ
    }.CreateConnectionAsync(TestContext.Current.CancellationToken);

    /// <summary>Fila exclusiva ligada a <c>oms.eventos</c> pela chave, guardando o que recebe.</summary>
    public async Task<FilaTeste> FilaTesteAsync(string chave)
    {
        var conexao = await ConectarAsync();
        var canal = await conexao.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        await TopologiaMensageria.DeclararTrocaAsync(canal, TestContext.Current.CancellationToken);
        var fila = await canal.QueueDeclareAsync(cancellationToken: TestContext.Current.CancellationToken);
        await canal.QueueBindAsync(fila.QueueName, TopologiaMensageria.TrocaEventos, chave, cancellationToken: TestContext.Current.CancellationToken);
        var teste = new FilaTeste(conexao, canal);
        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += (_, entrega) =>
        {
            // O corpo só vale durante o callback (a memória é reaproveitada): copia agora.
            teste.Mensagens.Enqueue(new MensagemRecebida(entrega.BasicProperties, entrega.Body.ToArray()));
            return Task.CompletedTask;
        };
        await canal.BasicConsumeAsync(fila.QueueName, autoAck: true, consumidor, TestContext.Current.CancellationToken);
        return teste;
    }

    /// <summary>
    /// Esvazia as filas do consumidor (principal, espera de 100, 200 e 400 ms e DLQ), se existirem.
    /// O broker é compartilhado pela coleção e as filas são duráveis: sem isso, um teste consome o
    /// que os anteriores publicaram (CI do PR #22).
    /// </summary>
    public async Task LimparFilasConsumidorAsync(string consumidor)
    {
        await using var conexao = await ConectarAsync();
        string[] filas =
        [
            TopologiaMensageria.FilaPrincipal(consumidor),
            .. new[] { 100, 200, 400 }.Select(ms => TopologiaMensageria.FilaEspera(consumidor, ms)),
            TopologiaMensageria.FilaMortas(consumidor)
        ];
        foreach (var fila in filas)
        {
            // Fila inexistente fecha o canal (404): um canal por fila.
            await using var canal = await conexao.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
            try
            {
                await canal.QueuePurgeAsync(fila, TestContext.Current.CancellationToken);
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                // Ainda não existe: nada a limpar.
            }
        }
    }

    /// <summary>
    /// Apaga as filas do consumidor (principal, espera de 100, 200 e 400 ms e DLQ), como num
    /// broker recriado (REQ-12). Fila inexistente é ignorada.
    /// </summary>
    public async Task ApagarFilasConsumidorAsync(string consumidor, bool soPrincipal = false)
    {
        await using var conexao = await ConectarAsync();
        string[] filas = soPrincipal
            ? [TopologiaMensageria.FilaPrincipal(consumidor)]
            :
            [
                TopologiaMensageria.FilaPrincipal(consumidor),
                .. new[] { 100, 200, 400 }.Select(ms => TopologiaMensageria.FilaEspera(consumidor, ms)),
                TopologiaMensageria.FilaMortas(consumidor)
            ];
        foreach (var fila in filas)
        {
            await using var canal = await conexao.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
            await canal.QueueDeleteAsync(fila, ifUnused: false, ifEmpty: false, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    /// <summary>Quantas mensagens a fila tem, ou nulo se ela não existe (REQ-12).</summary>
    public async Task<uint?> MensagensNaFilaAsync(string fila)
    {
        await using var conexao = await ConectarAsync();
        await using var canal = await conexao.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            return (await canal.QueueDeclarePassiveAsync(fila, TestContext.Current.CancellationToken)).MessageCount;
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
        {
            return null;
        }
    }

    /// <summary>Espera a fila principal do consumidor existir e ter consumidor ligado, até o prazo.</summary>
    public async Task<bool> ConsumidorLigadoAsync(string consumidor, TimeSpan prazo)
    {
        await using var conexao = await ConectarAsync();
        return await EsperarAteAsync(async () =>
        {
            // Fila ainda inexistente fecha o canal (404): um canal novo por tentativa.
            await using var canal = await conexao.CreateChannelAsync(cancellationToken: TestContext.Current.CancellationToken);
            try
            {
                return (await canal.QueueDeclarePassiveAsync(TopologiaMensageria.FilaPrincipal(consumidor), TestContext.Current.CancellationToken)).ConsumerCount > 0;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                return false;
            }
        }, prazo);
    }

    /// <summary>Espera a condição ficar verdadeira, verificando a cada 50 ms, até o prazo.</summary>
    public static async Task<bool> EsperarAteAsync(Func<Task<bool>> condicao, TimeSpan prazo)
    {
        var limite = DateTime.UtcNow + prazo;
        while (DateTime.UtcNow < limite)
        {
            if (await condicao())
            {
                return true;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        return await condicao();
    }

    public async ValueTask InitializeAsync()
    {
        try
        {
            sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04").Build();
            portaAmqp = PortaLivre();
            rabbitMQ = new RabbitMqBuilder("rabbitmq:4.3.6-management")
                .WithUsername(UsuarioRabbitMQ)
                .WithPassword(SenhaRabbitMQ)
                .WithPortBinding(portaAmqp, 5672)
                .Build();
            await Task.WhenAll(sqlServer.StartAsync(), rabbitMQ.StartAsync());
            await CriarUsuarioApiAsync();
            DockerDisponivel = true;
        }
        catch (DockerUnavailableException excecao)
        {
            // No CI (OMS_EXIGIR_DOCKER=true), sem Docker os testes falham em vez de serem ignorados (REQ-07).
            if (AmbienteDocker.Obrigatorio())
            {
                throw new InvalidOperationException(AmbienteDocker.MensagemObrigatorio, excecao);
            }

            DockerDisponivel = false;
        }
    }

    /// <summary>Uma porta TCP livre do host, escolhida pelo sistema operacional.</summary>
    private static int PortaLivre()
    {
        var ouvinte = new TcpListener(IPAddress.Loopback, 0);
        ouvinte.Start();
        try
        {
            return ((IPEndPoint)ouvinte.LocalEndpoint).Port;
        }
        finally
        {
            ouvinte.Stop();
        }
    }

    /// <summary>Como o script do compose (docker/rabbitmq/iniciar.sh): o usuário da API pode conectar, e nada mais.</summary>
    private async Task CriarUsuarioApiAsync()
    {
        foreach (string[] comando in new[]
        {
            new[] { "rabbitmqctl", "add_user", UsuarioRabbitMQApi, SenhaRabbitMQApi },
            new[] { "rabbitmqctl", "set_permissions", "-p", "/", UsuarioRabbitMQApi, "^$", "^$", "^$" }
        })
        {
            var resultado = await rabbitMQ!.ExecAsync(comando);
            if (resultado.ExitCode != 0)
            {
                throw new InvalidOperationException($"Falha ao criar o usuário da API no broker: {resultado.Stderr}");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (sqlServer is not null)
        {
            await sqlServer.DisposeAsync();
        }

        if (rabbitMQ is not null)
        {
            await rabbitMQ.DisposeAsync();
        }
    }
}

/// <summary>Mensagens recebidas por uma fila de teste.</summary>
public sealed class FilaTeste(IConnection conexao, IChannel canal) : IAsyncDisposable
{
    public ConcurrentQueue<MensagemRecebida> Mensagens { get; } = new();

    public async ValueTask DisposeAsync()
    {
        await canal.DisposeAsync();
        await conexao.DisposeAsync();
    }
}

/// <summary>Uma mensagem recebida, com o corpo já copiado.</summary>
public sealed record MensagemRecebida(IReadOnlyBasicProperties Propriedades, byte[] Corpo)
{
    public string Texto => System.Text.Encoding.UTF8.GetString(Corpo);
}
