using System.Collections.Concurrent;
using System.Globalization;
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

    private MsSqlContainer? sqlServer;
    private RabbitMqContainer? rabbitMQ;

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
        ["RabbitMQ:Porta"] = rabbitMQ.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
        ["RabbitMQ:Usuario"] = UsuarioRabbitMQ,
        ["RabbitMQ:Senha"] = SenhaRabbitMQ,
        ["RabbitMQ:VirtualHost"] = "/"
    };

    /// <summary>Simula o broker fora do ar sem trocar a porta mapeada.</summary>
    /// <summary>Credenciais da API no broker (REQ-14, R-09), no formato da seção RabbitMQ.</summary>
    public IReadOnlyDictionary<string, string?> ConfiguracoesBrokerApi => new Dictionary<string, string?>(ConfiguracoesBroker)
    {
        ["RabbitMQ:Usuario"] = UsuarioRabbitMQApi,
        ["RabbitMQ:Senha"] = SenhaRabbitMQApi
    };

    public Task<IConnection> ConectarComoApiAsync() => new ConnectionFactory
    {
        HostName = rabbitMQ!.Hostname,
        Port = rabbitMQ.GetMappedPublicPort(5672),
        UserName = UsuarioRabbitMQApi,
        Password = SenhaRabbitMQApi
    }.CreateConnectionAsync(TestContext.Current.CancellationToken);

    public Task PausarBrokerAsync() => rabbitMQ!.PauseAsync(TestContext.Current.CancellationToken);

    public Task RetomarBrokerAsync() => rabbitMQ!.UnpauseAsync(TestContext.Current.CancellationToken);

    public Task<IConnection> ConectarAsync() => new ConnectionFactory
    {
        HostName = rabbitMQ!.Hostname,
        Port = rabbitMQ.GetMappedPublicPort(5672),
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
            rabbitMQ = new RabbitMqBuilder("rabbitmq:4.3.6-management")
                .WithUsername(UsuarioRabbitMQ)
                .WithPassword(SenhaRabbitMQ)
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
