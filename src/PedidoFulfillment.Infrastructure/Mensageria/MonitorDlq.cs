using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Observabilidade;
using RabbitMQ.Client.Exceptions;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Aviso de tamanho da DLQ de cada consumidor conhecido (REQ-14, R-08). Ler o tamanho é
/// idempotente: não precisa de lock, e duas instâncias só repetem o aviso. Com a telemetria
/// ligada (REQ-16, clarify 2), lê a cada <c>ReenvioDlq:MetricaIntervaloSegundos</c> para a
/// métrica 3 e avisa no máximo uma vez por <c>AvisoIntervaloSegundos</c> por consumidor;
/// desligada, lê e avisa a cada <c>AvisoIntervaloSegundos</c>, como antes.
/// </summary>
internal sealed class MonitorDlq(
    ConexaoRabbitMQ conexao, IOptions<OpcoesReenvioDlq> opcoes, IConfiguration configuracaoGeral, TimeProvider relogio, ILogger<MonitorDlq> logger)
    : BackgroundService
{
    private readonly bool telemetria = OpcoesObservabilidade.Ligada(configuracaoGeral);
    private readonly Dictionary<string, DateTimeOffset> ultimosAvisos = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuracao = opcoes.Value;
        if (!configuracao.AvisoHabilitado && !telemetria)
        {
            return;
        }

        using var temporizador = new PeriodicTimer(telemetria ? configuracao.MetricaIntervalo : configuracao.AvisoIntervalo, relogio);
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

                EstadoMetricas.RegistrarDlq(consumidor, quantidade);
                if (configuracao.AvisoHabilitado && quantidade > configuracao.AvisoLimite && PodeAvisar(consumidor, configuracao))
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

    /// <summary>Com a leitura mais frequente da telemetria, o aviso continua no ritmo de antes (FR-018).</summary>
    private bool PodeAvisar(string consumidor, OpcoesReenvioDlq configuracao)
    {
        if (!telemetria)
        {
            return true;
        }

        var agora = relogio.GetUtcNow();
        if (ultimosAvisos.TryGetValue(consumidor, out var ultimo) && agora - ultimo < configuracao.AvisoIntervalo)
        {
            return false;
        }

        ultimosAvisos[consumidor] = agora;
        return true;
    }
}
