using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;
using RabbitMQ.Client;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Conexão única do processo com o broker (REQ-06, R-09), criada na primeira vez que alguém a
/// pede e com recuperação automática. Cada componente abre os próprios canais.
/// </summary>
internal sealed class ConexaoRabbitMQ(IOptions<OpcoesRabbitMQ> opcoes) : IAsyncDisposable
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
                await conexao.DisposeAsync();
            }

            var configuracao = opcoes.Value;
            var fabrica = new ConnectionFactory
            {
                HostName = configuracao.Servidor,
                Port = configuracao.Porta,
                UserName = configuracao.Usuario,
                Password = configuracao.Senha,
                VirtualHost = configuracao.VirtualHost,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ClientProvidedName = "oms-worker",
                // Prazos curtos: com o broker sem responder, a falha volta logo para quem chamou.
                RequestedConnectionTimeout = TimeSpan.FromSeconds(5),
                ContinuationTimeout = TimeSpan.FromSeconds(5),
                RequestedHeartbeat = TimeSpan.FromSeconds(10)
            };
            conexao = await fabrica.CreateConnectionAsync(cancellationToken);
            return conexao;
        }
        finally
        {
            trava.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (conexao is not null)
        {
            await conexao.DisposeAsync();
        }

        trava.Dispose();
    }
}
