using System.Diagnostics;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Observabilidade;
using PedidoFulfillment.Infrastructure.Persistencia;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Base de consumo de um <see cref="IManipuladorEvento{TDados}"/> (REQ-06, R-07;
/// <c>contracts/topologia-e-consumo.md</c>):
/// <list type="bullet">
/// <item>mensagem ilegível vai direto para a DLQ (FR-014);</item>
/// <item>evento já processado por este consumidor é confirmado sem repetir o efeito (FR-015);</item>
/// <item>o efeito e o registro de processado são gravados na mesma transação;</item>
/// <item>falha volta pela fila de espera do próximo intervalo, sem bloquear a fila, até 3 vezes;
/// depois, DLQ com o motivo (FR-012, FR-013);</item>
/// <item>o ack só vem depois disso tudo (FR-016).</item>
/// </list>
/// </summary>
internal sealed partial class ConsumidorEventos<TManipulador, TDados>(
    IServiceScopeFactory escopos,
    ConexaoRabbitMQ conexao,
    IOptions<OpcoesMensageria> opcoes,
    TimeProvider relogio,
    ILogger<ConsumidorEventos<TManipulador, TDados>> logger) : BackgroundService
    where TManipulador : class, IManipuladorEvento<TDados>
{
    private static readonly TimeSpan EsperaReconexao = TimeSpan.FromSeconds(2);

    /// <summary>Fonte do span de entrega (<c>deliver</c>) criado pelo RabbitMQ.Client 7 (REQ-16, R-02).</summary>
    private const string FonteEntrega = "RabbitMQ.Client.Subscriber";

    /// <summary><c>error.type</c> de mensagem ilegível, que vai direto para a DLQ.</summary>
    private const string ErroIrrecuperavel = "MensagemIrrecuperavel";

    private string consumidor = "";
    private IReadOnlyCollection<string> tipos = [];
    private IChannel? canalConsumo;
    private IChannel? canalPublicacao;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using (var escopo = escopos.CreateAsyncScope())
        {
            var manipulador = escopo.ServiceProvider.GetRequiredService<TManipulador>();
            consumidor = manipulador.Consumidor;
            tipos = [.. manipulador.Tipos];
        }

        // O broker pode ainda não estar no ar: tenta até conseguir, sem derrubar o Worker.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await IniciarAsync(stoppingToken);
                break;
            }
            catch (Exception excecao) when (!stoppingToken.IsCancellationRequested)
            {
                FalhaInicio(logger, consumidor, excecao.GetType().Name, excecao.Message);
                await Task.Delay(EsperaReconexao, stoppingToken);
            }
        }

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Encerramento do Worker.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        foreach (var canal in new[] { canalConsumo, canalPublicacao })
        {
            if (canal is not null)
            {
                await canal.DisposeAsync();
            }
        }
    }

    private async Task IniciarAsync(CancellationToken cancellationToken)
    {
        var conexaoAberta = await conexao.ObterAsync(cancellationToken);
        canalPublicacao = await conexaoAberta.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
        canalConsumo = await conexaoAberta.CreateChannelAsync(cancellationToken: cancellationToken);
        await TopologiaMensageria.DeclararConsumidorAsync(canalConsumo, consumidor, tipos, opcoes.Value.Intervalos, cancellationToken);
        await canalConsumo.BasicQosAsync(0, (ushort)opcoes.Value.PrefetchConsumidor, false, cancellationToken);
        var consumidorRabbit = new AsyncEventingBasicConsumer(canalConsumo);
        consumidorRabbit.ReceivedAsync += (_, entrega) => ProcessarAsync(entrega, cancellationToken);
        await canalConsumo.BasicConsumeAsync(TopologiaMensageria.FilaPrincipal(consumidor), autoAck: false, consumidorRabbit, cancellationToken);
    }

    private async Task ProcessarAsync(BasicDeliverEventArgs entrega, CancellationToken cancellationToken)
    {
        // O corpo só vale durante o callback: copia antes de qualquer espera.
        var corpo = entrega.Body.ToArray();
        var tentativas = PoliticaNovaTentativa.TentativasFeitas(entrega.BasicProperties.Headers);
        // O span de entrega do RabbitMQ.Client, filho da publicação (REQ-16, R-02): só marcado aqui.
        var span = Activity.Current is { Source.Name: FonteEntrega } entregaAtual ? entregaAtual : null;
        span?.SetTag(TelemetriaOms.Atributos.Consumidor, consumidor);
        span?.SetTag(TelemetriaOms.Atributos.EventoTipo, entrega.BasicProperties.Type);
        span?.SetTag(TelemetriaOms.Atributos.Tentativa, tentativas);
        var leitura = LeitorMensagem.Ler<TDados>(corpo, tipos);
        if (leitura.Envelope is not { } envelope)
        {
            await PublicarAsync(TopologiaMensageria.FilaMortas(consumidor), corpo, entrega.BasicProperties, DlqCabecalhos(tentativas, leitura.Motivo!), cancellationToken);
            EnviadaParaDlq(logger, entrega.BasicProperties.MessageId, consumidor, tentativas, leitura.Motivo!);
            span?.SetTag(TelemetriaOms.Atributos.Resultado, TelemetriaOms.Resultados.Irrecuperavel);
            span.MarcarErro(ErroIrrecuperavel);
            await canalConsumo!.BasicAckAsync(entrega.DeliveryTag, multiple: false, cancellationToken);
            return;
        }

        span?.SetTag(TelemetriaOms.Atributos.PedidoId, envelope.IdCorrelacao.ToString());
        try
        {
            if (await ManipularAsync(envelope, cancellationToken))
            {
                EventoProcessado(logger, envelope.IdEvento, consumidor);
                span?.SetTag(TelemetriaOms.Atributos.Resultado, TelemetriaOms.Resultados.Processado);
            }
            else
            {
                DuplicadoDescartado(logger, envelope.IdEvento, consumidor);
                span?.SetTag(TelemetriaOms.Atributos.Resultado, TelemetriaOms.Resultados.Duplicado);
            }
        }
        catch (Exception excecao) when (!cancellationToken.IsCancellationRequested)
        {
            var motivo = $"{excecao.GetType().Name}: {excecao.Message}";
            if (PoliticaNovaTentativa.ProximaEspera(tentativas, opcoes.Value.Intervalos) is { } espera)
            {
                var cabecalhos = new Dictionary<string, object?>
                {
                    [PoliticaNovaTentativa.CabecalhoTentativas] = tentativas + 1,
                    [PoliticaNovaTentativa.CabecalhoUltimoErro] = motivo
                };
                await PublicarAsync(TopologiaMensageria.FilaEspera(consumidor, espera), corpo, entrega.BasicProperties, cabecalhos, cancellationToken);
                NovaTentativa(logger, envelope.IdEvento, consumidor, tentativas + 1, espera, motivo);
                span?.SetTag(TelemetriaOms.Atributos.Resultado, TelemetriaOms.Resultados.NovaTentativa);
            }
            else
            {
                await PublicarAsync(TopologiaMensageria.FilaMortas(consumidor), corpo, entrega.BasicProperties, DlqCabecalhos(tentativas, motivo), cancellationToken);
                EnviadaParaDlq(logger, envelope.IdEvento.ToString(), consumidor, tentativas, motivo);
                span?.SetTag(TelemetriaOms.Atributos.Resultado, TelemetriaOms.Resultados.Dlq);
            }

            span.MarcarErro(excecao.GetType().Name);
        }

        await canalConsumo!.BasicAckAsync(entrega.DeliveryTag, multiple: false, cancellationToken);
    }

    /// <summary>Verdadeiro se o manipulador rodou; falso se o evento já tinha sido processado.</summary>
    private async Task<bool> ManipularAsync(Application.Contratos.Eventos.EnvelopeEvento<TDados> envelope, CancellationToken cancellationToken)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
        await using var transacao = await contexto.Database.BeginTransactionAsync(cancellationToken);
        if (await contexto.EventosProcessados.AnyAsync(p => p.Consumidor == consumidor && p.IdEvento == envelope.IdEvento, cancellationToken))
        {
            await transacao.CommitAsync(cancellationToken);
            return false;
        }

        // O manipulador grava pelo mesmo contexto do escopo: efeito e registro entram juntos no commit.
        await escopo.ServiceProvider.GetRequiredService<TManipulador>().ManipularAsync(envelope, cancellationToken);
        contexto.EventosProcessados.Add(new EventoProcessado(consumidor, envelope.IdEvento, relogio.GetUtcNow()));
        await contexto.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);
        return true;
    }

    private Dictionary<string, object?> DlqCabecalhos(int tentativas, string motivo) => new()
    {
        [PoliticaNovaTentativa.CabecalhoTentativas] = tentativas,
        [PoliticaNovaTentativa.CabecalhoMotivo] = motivo,
        [PoliticaNovaTentativa.CabecalhoFalhouEm] = relogio.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)
    };

    /// <summary>Republica com confirmação; só depois a original recebe ack.</summary>
    private async Task PublicarAsync(
        string fila, byte[] corpo, IReadOnlyBasicProperties original, Dictionary<string, object?> cabecalhos, CancellationToken cancellationToken)
    {
        var todos = new Dictionary<string, object?>(original.Headers ?? new Dictionary<string, object?>());
        foreach (var (chave, valor) in cabecalhos)
        {
            todos[chave] = valor;
        }

        var propriedades = new BasicProperties
        {
            Persistent = true,
            MessageId = original.MessageId,
            Type = original.Type,
            ContentType = original.ContentType,
            Headers = todos
        };
        await canalPublicacao!.BasicPublishAsync("", fila, mandatory: false, propriedades, corpo, cancellationToken);
    }

    [LoggerMessage(EventId = 5003, EventName = "NovaTentativa", Level = LogLevel.Warning,
        Message = "Evento {IdEvento} falhou em {Consumidor}; nova tentativa {Tentativa} em {EsperaMs} ms: {Motivo}")]
    private static partial void NovaTentativa(ILogger logger, Guid idEvento, string consumidor, int tentativa, int esperaMs, string motivo);

    [LoggerMessage(EventId = 5004, EventName = "EnviadaParaDlq", Level = LogLevel.Error,
        Message = "Mensagem {IdEvento} enviada para a DLQ de {Consumidor} após {Tentativas} nova(s) tentativa(s): {Motivo}")]
    private static partial void EnviadaParaDlq(ILogger logger, string? idEvento, string consumidor, int tentativas, string motivo);

    [LoggerMessage(EventId = 5005, EventName = "DuplicadoDescartado", Level = LogLevel.Information,
        Message = "Evento {IdEvento} já processado por {Consumidor}; descartado.")]
    private static partial void DuplicadoDescartado(ILogger logger, Guid idEvento, string consumidor);

    [LoggerMessage(EventId = 5006, EventName = "EventoProcessado", Level = LogLevel.Information,
        Message = "Evento {IdEvento} processado por {Consumidor}.")]
    private static partial void EventoProcessado(ILogger logger, Guid idEvento, string consumidor);

    [LoggerMessage(EventId = 5008, EventName = "FalhaInicioConsumidor", Level = LogLevel.Warning,
        Message = "Consumidor {Consumidor} não iniciou ({Erro}): {Mensagem}. Nova tentativa em instantes.")]
    private static partial void FalhaInicio(ILogger logger, string consumidor, string erro, string mensagem);
}
