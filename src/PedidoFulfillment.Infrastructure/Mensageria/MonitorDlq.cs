using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Infrastructure.Configuracao;
using RabbitMQ.Client.Exceptions;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Aviso de tamanho da DLQ de cada consumidor conhecido (REQ-14, R-08). Ler o tamanho é
/// idempotente: não precisa de lock, e duas instâncias só repetem o aviso.
/// </summary>
internal sealed class MonitorDlq(
    ConexaoRabbitMQ conexao, IOptions<OpcoesReenvioDlq> opcoes, TimeProvider relogio, ILogger<MonitorDlq> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuracao = opcoes.Value;
        if (!configuracao.AvisoHabilitado)
        {
            return;
        }

        using var temporizador = new PeriodicTimer(configuracao.AvisoIntervalo, relogio);
        try
        {
            do
            {
                await VerificarAsync(configuracao, stoppingToken);
            }
            while (await temporizador.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento do Worker.
        }
    }

    internal async Task VerificarAsync(OpcoesReenvioDlq configuracao, CancellationToken cancellationToken)
    {
        try
        {
            var conexaoAberta = await conexao.ObterAsync(cancellationToken);
            foreach (var consumidor in ConsumidoresConhecidos.Nomes)
            {
                // Fila inexistente (broker recriado) fecha o canal com 404: um canal por fila, e conta como zero.
                await using var canal = await conexaoAberta.CreateChannelAsync(cancellationToken: cancellationToken);
                uint quantidade;
                try
                {
                    quantidade = (await canal.QueueDeclarePassiveAsync(TopologiaMensageria.FilaMortas(consumidor), cancellationToken)).MessageCount;
                }
                catch (OperationInterruptedException excecao) when (excecao.ShutdownReason?.ReplyCode == 404)
                {
                    quantidade = 0;
                }

                if (quantidade > configuracao.AvisoLimite)
                {
                    EventosLimpezaReenvio.DlqAcimaDoLimite(logger, consumidor, quantidade, configuracao.AvisoLimite);
                }
            }
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            EventosLimpezaReenvio.FalhaVerificacaoDlq(logger, excecao.GetType().Name, excecao.Message);
        }
    }
}
