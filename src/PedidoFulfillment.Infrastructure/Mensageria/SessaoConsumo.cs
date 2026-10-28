using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>Por que uma sessão de consumo terminou (REQ-17, data-model).</summary>
internal sealed record MotivoPerda(string Origem, string Motivo)
{
    /// <summary>Canal de consumo ou de publicação fechado (conexão perdida, broker parado, erro de canal).</summary>
    public const string Canal = "canal";

    /// <summary>Inscrição cancelada pelo broker (fila apagada), com o canal ainda aberto.</summary>
    public const string Cancelamento = "cancelamento";
}

/// <summary>O que o <see cref="SupervisorInscricao"/> precisa de uma sessão: saber quando ela acabou e fechá-la.</summary>
internal interface ISessaoConsumo : IAsyncDisposable
{
    Task<MotivoPerda> Perdida { get; }
}

/// <summary>
/// Uma inscrição de consumidor (REQ-17, R-03 e R-05): canal de publicação com confirmação, canal
/// de consumo com a topologia declarada e o <c>basic.consume</c>. Termina no primeiro sinal de
/// perda; quem entregou uma mensagem confirma e republica pelos canais desta sessão, porque o
/// <c>delivery tag</c> só vale no canal que entregou.
/// </summary>
internal sealed class SessaoConsumo : ISessaoConsumo
{
    private static readonly TimeSpan PrazoFechamento = TimeSpan.FromSeconds(2);

    private readonly TaskCompletionSource<MotivoPerda> perda = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool encerrando;

    private SessaoConsumo(IChannel canalConsumo, IChannel canalPublicacao)
    {
        CanalConsumo = canalConsumo;
        CanalPublicacao = canalPublicacao;
        canalConsumo.ChannelShutdownAsync += AoFecharCanalAsync;
        canalPublicacao.ChannelShutdownAsync += AoFecharCanalAsync;
        // Um canal que fechou antes da assinatura não dispararia o evento.
        if (!canalConsumo.IsOpen || !canalPublicacao.IsOpen)
        {
            perda.TrySetResult(new MotivoPerda(MotivoPerda.Canal, "canal fechado na abertura"));
        }
    }

    public IChannel CanalConsumo { get; }

    public IChannel CanalPublicacao { get; }

    public Task<MotivoPerda> Perdida => perda.Task;

    /// <summary>
    /// Abre os canais, declara a topologia do consumidor e se inscreve na fila principal. Se algo
    /// falhar no meio, fecha o que já abriu e relança.
    /// </summary>
    public static async Task<SessaoConsumo> AbrirAsync(
        IConnection conexao,
        string fila,
        ushort prefetch,
        Func<IChannel, CancellationToken, Task> declararTopologia,
        Func<SessaoConsumo, BasicDeliverEventArgs, Task> aoReceber,
        CancellationToken cancellationToken)
    {
        IChannel? canalPublicacao = null;
        IChannel? canalConsumo = null;
        try
        {
            canalPublicacao = await conexao.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
            canalConsumo = await conexao.CreateChannelAsync(cancellationToken: cancellationToken);
            await declararTopologia(canalConsumo, cancellationToken);
            await canalConsumo.BasicQosAsync(0, prefetch, false, cancellationToken);
            var sessao = new SessaoConsumo(canalConsumo, canalPublicacao);
            var consumidor = new AsyncEventingBasicConsumer(canalConsumo);
            consumidor.ReceivedAsync += (_, entrega) => aoReceber(sessao, entrega);
            consumidor.UnregisteredAsync += sessao.AoCancelarAsync;
            await canalConsumo.BasicConsumeAsync(fila, autoAck: false, consumidor, cancellationToken);
            return sessao;
        }
        catch
        {
            await FecharAsync(canalConsumo);
            await FecharAsync(canalPublicacao);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        encerrando = true;
        perda.TrySetResult(new MotivoPerda(MotivoPerda.Canal, "sessão encerrada"));
        await FecharAsync(CanalConsumo);
        await FecharAsync(CanalPublicacao);
    }

    private Task AoFecharCanalAsync(object? remetente, ShutdownEventArgs argumentos)
    {
        if (!encerrando)
        {
            perda.TrySetResult(new MotivoPerda(MotivoPerda.Canal, $"{argumentos.ReplyCode} {argumentos.ReplyText}"));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// O broker cancela a inscrição quando a fila é apagada (<c>basic.cancel</c>), e o canal
    /// continua aberto. Esta sessão nunca cancela a própria inscrição: fecha o canal.
    /// </summary>
    private Task AoCancelarAsync(object? remetente, ConsumerEventArgs argumentos)
    {
        if (!encerrando)
        {
            perda.TrySetResult(new MotivoPerda(MotivoPerda.Cancelamento, "cancelada pelo broker"));
        }

        return Task.CompletedTask;
    }

    /// <summary>Fecha sem esperar o broker, no máximo 2 s, e sem relançar: o canal pode já estar morto.</summary>
    private static async Task FecharAsync(IChannel? canal)
    {
        if (canal is null)
        {
            return;
        }

        try
        {
            using var prazo = new CancellationTokenSource(PrazoFechamento);
            await canal.CloseAsync(200, "sessao encerrada", abort: true, prazo.Token);
        }
        catch (Exception)
        {
            // Canal já fechado ou broker fora: nada a fazer.
        }

        try
        {
            await canal.DisposeAsync();
        }
        catch (Exception)
        {
            // Idem.
        }
    }
}
