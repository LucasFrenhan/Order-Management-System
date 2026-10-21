using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;
using RabbitMQ.Client;

namespace PedidoFulfillment.Infrastructure.Saude;

/// <summary>
/// Prontidão do broker: abre e fecha uma conexão AMQP em no máximo 5 segundos (FR-018).
/// </summary>
internal sealed class VerificacaoRabbitMQ(IOptions<OpcoesRabbitMQ> opcoes) : IHealthCheck
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limite.CancelAfter(Limite);
        var configuracao = opcoes.Value;
        var fabrica = new ConnectionFactory
        {
            HostName = configuracao.Servidor,
            Port = configuracao.Porta,
            UserName = configuracao.Usuario,
            Password = configuracao.Senha,
            VirtualHost = configuracao.VirtualHost,
            RequestedConnectionTimeout = Limite,
            HandshakeContinuationTimeout = Limite,
            AutomaticRecoveryEnabled = false
        };
        Task<IConnection>? abertura = null;
        try
        {
            abertura = fabrica.CreateConnectionAsync(limite.Token);
            // O cliente não respeita o token durante o handshake (servidor que aceita a conexão
            // e não responde leva ~10 s); WaitAsync garante o limite do FR-018.
            await using var conexao = await abertura.WaitAsync(limite.Token);
            await conexao.CloseAsync(limite.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception)
        {
            if (abertura is { IsCompleted: false })
            {
                DescartarSeAbrirDepois(abertura);
            }

            // Sem descrição nem exceção: nada de dado de conexão pode chegar à resposta.
            return HealthCheckResult.Unhealthy();
        }
    }

    private static void DescartarSeAbrirDepois(Task<IConnection> abertura) =>
        _ = abertura.ContinueWith(
            tarefa => tarefa.Result.DisposeAsync().AsTask(),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
}
