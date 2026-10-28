using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Conexão única do processo com o broker (REQ-06, R-09), criada na primeira vez que alguém a
/// pede. Cada componente abre os próprios canais. Sem a recuperação automática do RabbitMQ.Client
/// (REQ-17, R-02): o processo é dono da recuperação. Uma conexão fechada está morta, e a próxima
/// chamada a descarta e cria outra; os consumidores voltam pelo <see cref="SupervisorInscricao"/>,
/// e o despachante, o monitor e o reenvio da DLQ abrem canais novos.
/// </summary>
internal sealed partial class ConexaoRabbitMQ(IOptions<OpcoesRabbitMQ> opcoes, ILogger<ConexaoRabbitMQ> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim trava = new(1, 1);
    private IConnection? conexao;

    public async Task<IConnection> ObterAsync(CancellationToken cancellationToken)
    {
        if (conexao is { IsOpen: true })
        {
            return conexao;
        }

        await trava.WaitAsync(cancellationToken);
        try
        {
            if (conexao is { IsOpen: true })
            {
                return conexao;
            }

            if (conexao is not null)
            {
                var morta = conexao;
                conexao = null;
                await morta.DisposeAsync();
            }

            var configuracao = opcoes.Value;
            var fabrica = new ConnectionFactory
            {
                HostName = configuracao.Servidor,
                Port = configuracao.Porta,
                UserName = configuracao.Usuario,
                Password = configuracao.Senha,
                VirtualHost = configuracao.VirtualHost,
                // Com a recuperação do cliente, uma conexão em recuperação parecia fechada e era
                // descartada no meio, levando as inscrições junto (issue #41; REQ-17, R-01).
                AutomaticRecoveryEnabled = false,
                TopologyRecoveryEnabled = false,
                ClientProvidedName = "oms-worker",
                // Prazos curtos: com o broker sem responder, a falha volta logo para quem chamou.
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                ContinuationTimeout = TimeSpan.FromSeconds(5),
                RequestedHeartbeat = TimeSpan.FromSeconds(10)
            };
            var nova = await fabrica.CreateConnectionAsync(cancellationToken);
            nova.ConnectionShutdownAsync += AoFecharAsync;
            conexao = nova;
            return nova;
        }
        finally
        {
            trava.Release();
        }
    }

    /// <summary>Só o fechamento que o processo não pediu (broker parado, conexão fechada à força, rede).</summary>
    private Task AoFecharAsync(object? remetente, ShutdownEventArgs argumentos)
    {
        if (argumentos.Initiator != ShutdownInitiator.Application)
        {
            ConexaoBrokerFechada(logger, argumentos.Initiator.ToString(), argumentos.ReplyCode, argumentos.ReplyText);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (conexao is not null)
        {
            await conexao.DisposeAsync();
        }

        trava.Dispose();
    }

    [LoggerMessage(EventId = 5019, EventName = "ConexaoBrokerFechada", Level = LogLevel.Warning,
        Message = "Conexão com o broker fechada ({Iniciador} {Codigo}): {Motivo}")]
    private static partial void ConexaoBrokerFechada(ILogger logger, string iniciador, ushort codigo, string motivo);
}
