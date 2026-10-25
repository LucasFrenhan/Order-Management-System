using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Infrastructure.Configuracao;
using RabbitMQ.Client;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>A execução parou no meio (o canal caiu); as contagens são as feitas até ali.</summary>
internal sealed class ReenvioInterrompidoException(ContagensReenvio parciais, Exception causa)
    : Exception($"O reenvio parou no meio: {causa.GetType().Name}: {causa.Message}", causa)
{
    public ContagensReenvio Parciais { get; } = parciais;
}

/// <summary>
/// Executa um pedido de reenvio no broker (REQ-14, R-04): lê a cabeça da DLQ sem ack, reenvia as
/// selecionadas para a fila original com confirmação e só então dá ack, e devolve as outras com
/// <c>BasicNack(requeue)</c>, que as recoloca na posição original (filas clássicas). Se o canal
/// cai no meio, o broker devolve sozinho as entregas sem ack: nenhuma mensagem se perde.
/// </summary>
internal sealed class ReenvioDlq(ConexaoRabbitMQ conexao, IOptions<OpcoesReenvioDlq> opcoes, TimeProvider relogio)
{
    /// <summary>Canal com confirmações rastreadas; aberto antes de marcar o pedido (analyze I2).</summary>
    public async Task<IChannel> AbrirCanalAsync(CancellationToken cancellationToken)
    {
        var conexaoAberta = await conexao.ObterAsync(cancellationToken);
        return await conexaoAberta.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
    }

    /// <exception cref="ReenvioInterrompidoException">O canal caiu no meio.</exception>
    public async Task<ContagensReenvio> ExecutarAsync(IChannel canal, PedidoReenvioDlq pedido, CancellationToken cancellationToken)
    {
        var configuracao = opcoes.Value;
        var dlq = TopologiaMensageria.FilaMortas(pedido.Consumidor);
        var destino = TopologiaMensageria.FilaPrincipal(pedido.Consumidor);
        var filtro = pedido.Filtro;
        var devolver = new List<ulong>();
        int reenviadas = 0, bloqueadas = 0, foraDoFiltro = 0;
        try
        {
            // Só as mensagens que já estavam na DLQ: uma reenviada que falha de novo volta para o fim
            // da fila durante a execução e não pode ser reenviada outra vez pelo mesmo pedido.
            if (await ContarAsync(dlq, cancellationToken) is not { } naDlq)
            {
                return new ContagensReenvio(0, 0, 0);
            }

            var limite = (int)Math.Min(naDlq, (uint)configuracao.MensagensPorPedido);
            for (var lidas = 0; lidas < limite; lidas++)
            {
                if (await canal.BasicGetAsync(dlq, autoAck: false, cancellationToken) is not { } mensagem)
                {
                    break;
                }

                var propriedades = mensagem.BasicProperties;
                var cabecalhos = propriedades.Headers;
                if (CabecalhosReenvio.PedidoReenvioId(cabecalhos) == pedido.Id)
                {
                    // Já reenviada por este pedido e de volta: fica para um próximo pedido.
                    devolver.Add(mensagem.DeliveryTag);
                    continue;
                }

                if (!filtro.Atende(propriedades.Type, CabecalhosReenvio.FalhouEm(cabecalhos), CabecalhosReenvio.Motivo(cabecalhos)))
                {
                    foraDoFiltro++;
                    devolver.Add(mensagem.DeliveryTag);
                    continue;
                }

                if (CabecalhosReenvio.ReenviosFeitos(cabecalhos) >= configuracao.MaximoReenvios)
                {
                    bloqueadas++;
                    devolver.Add(mensagem.DeliveryTag);
                    continue;
                }

                var copia = new BasicProperties
                {
                    Persistent = true,
                    MessageId = propriedades.MessageId,
                    Type = propriedades.Type,
                    ContentType = propriedades.ContentType,
                    Headers = CabecalhosReenvio.ParaReenvio(cabecalhos, pedido.Id, relogio.GetUtcNow())
                };

                // Com as confirmações rastreadas, a chamada só volta depois do ack do broker; sem fila, lança.
                await canal.BasicPublishAsync("", destino, mandatory: true, copia, mensagem.Body, cancellationToken);
                await canal.BasicAckAsync(mensagem.DeliveryTag, multiple: false, cancellationToken);
                reenviadas++;
            }

            foreach (var tag in devolver)
            {
                await canal.BasicNackAsync(tag, multiple: false, requeue: true, cancellationToken);
            }

            return new ContagensReenvio(reenviadas, bloqueadas, foraDoFiltro);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            throw new ReenvioInterrompidoException(new ContagensReenvio(reenviadas, bloqueadas, foraDoFiltro), excecao);
        }
    }

    /// <summary>Mensagens na fila agora, ou nulo se ela não existe (o 404 fecha o canal: confere num canal próprio).</summary>
    private async Task<uint?> ContarAsync(string fila, CancellationToken cancellationToken)
    {
        var conexaoAberta = await conexao.ObterAsync(cancellationToken);
        await using var verificacao = await conexaoAberta.CreateChannelAsync(cancellationToken: cancellationToken);
        try
        {
            return (await verificacao.QueueDeclarePassiveAsync(fila, cancellationToken)).MessageCount;
        }
        catch (RabbitMQ.Client.Exceptions.OperationInterruptedException excecao) when (excecao.ShutdownReason?.ReplyCode == 404)
        {
            return null;
        }
    }
}
