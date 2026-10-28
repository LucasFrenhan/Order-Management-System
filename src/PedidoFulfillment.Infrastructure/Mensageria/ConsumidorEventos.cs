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
using RabbitMQ.Client.Exceptions;

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
/// A inscrição é mantida pelo <see cref="SupervisorInscricao"/> (REQ-17): perdida por queda do
/// broker, conexão fechada, canal fechado ou fila apagada, ela é refeita sem reiniciar o Worker.
/// </summary>
internal sealed partial class ConsumidorEventos<TManipulador, TDados>(
    IServiceScopeFactory escopos,
    ConexaoRabbitMQ conexao,
    EstadoConsumidores estado,
    IOptions<OpcoesMensageria> opcoes,
    TimeProvider relogio,
    ILogger<ConsumidorEventos<TManipulador, TDados>> logger) : BackgroundService
    where TManipulador : class, IManipuladorEvento<TDados>
{
    /// <summary>Fonte do span de entrega (<c>deliver</c>) criado pelo RabbitMQ.Client 7 (REQ-16, R-02).</summary>
    private const string FonteEntrega = "RabbitMQ.Client.Subscriber";

    /// <summary><c>error.type</c> de mensagem ilegível, que vai direto para a DLQ.</summary>
    private const string ErroIrrecuperavel = "MensagemIrrecuperavel";

    private string consumidor = "";
    private IReadOnlyCollection<string> tipos = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using (var escopo = escopos.CreateAsyncScope())
        {
            var manipulador = escopo.ServiceProvider.GetRequiredService<TManipulador>();
            consumidor = manipulador.Consumidor;
            tipos = [.. manipulador.Tipos];
        }

        // O broker pode ainda não estar no ar, ou cair depois: o supervisor tenta até conseguir,
        // sem derrubar o Worker, e refaz a inscrição a cada perda (REQ-17).
        var supervisor = new SupervisorInscricao(consumidor, AbrirSessaoAsync, Task.Delay, estado, relogio, logger);
        await supervisor.ExecutarAsync(stoppingToken);
    }

    private async Task<ISessaoConsumo> AbrirSessaoAsync(CancellationToken cancellationToken)
    {
        var conexaoAberta = await conexao.ObterAsync(cancellationToken);
        return await SessaoConsumo.AbrirAsync(
            conexaoAberta,
            TopologiaMensageria.FilaPrincipal(consumidor),
            (ushort)opcoes.Value.PrefetchConsumidor,
            (canal, token) => TopologiaMensageria.DeclararConsumidorAsync(canal, consumidor, tipos, opcoes.Value.Intervalos, token),
            (sessao, entrega) => ProcessarAsync(sessao, entrega, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// Com o canal fechado no meio do tratamento (queda do broker), o ack ou a republicação falham:
    /// a mensagem não é confirmada, e o broker a devolve à fila com os cabeçalhos de antes. Se o
    /// efeito já foi gravado, a reentrega é descartada como duplicada (REQ-17, R-05).
    /// </summary>
    private async Task ProcessarAsync(SessaoConsumo sessao, BasicDeliverEventArgs entrega, CancellationToken cancellationToken)
    {
        try
        {
            await TratarAsync(sessao, entrega, cancellationToken);
        }
        catch (Exception excecao) when (excecao is AlreadyClosedException or OperationInterruptedException && !sessao.CanalConsumo.IsOpen)
        {
            // Canal da sessão fechado: o supervisor abre outra, e a mensagem volta pelo broker.
        }
    }

    private async Task TratarAsync(SessaoConsumo sessao, BasicDeliverEventArgs entrega, CancellationToken cancellationToken)
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
            await PublicarAsync(sessao, TopologiaMensageria.FilaMortas(consumidor), corpo, entrega.BasicProperties, DlqCabecalhos(tentativas, leitura.Motivo!), cancellationToken);
            EnviadaParaDlq(logger, entrega.BasicProperties.MessageId, consumidor, tentativas, leitura.Motivo!);
            Registrar(span, TelemetriaOms.Resultados.Irrecuperavel);
            span.MarcarErro(ErroIrrecuperavel);
            await sessao.CanalConsumo.BasicAckAsync(entrega.DeliveryTag, multiple: false, cancellationToken);
            return;
        }

        span?.SetTag(TelemetriaOms.Atributos.PedidoId, envelope.IdCorrelacao.ToString());
        try
        {
            if (await ManipularAsync(envelope, cancellationToken))
            {
                EventoProcessado(logger, envelope.IdEvento, consumidor);
                Registrar(span, TelemetriaOms.Resultados.Processado);
            }
            else
            {
                DuplicadoDescartado(logger, envelope.IdEvento, consumidor);
                Registrar(span, TelemetriaOms.Resultados.Duplicado);
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
                await PublicarAsync(sessao, TopologiaMensageria.FilaEspera(consumidor, espera), corpo, entrega.BasicProperties, cabecalhos, cancellationToken);
                NovaTentativa(logger, envelope.IdEvento, consumidor, tentativas + 1, espera, motivo);
                Registrar(span, TelemetriaOms.Resultados.NovaTentativa);
            }
            else
            {
                await PublicarAsync(sessao, TopologiaMensageria.FilaMortas(consumidor), corpo, entrega.BasicProperties, DlqCabecalhos(tentativas, motivo), cancellationToken);
                EnviadaParaDlq(logger, envelope.IdEvento.ToString(), consumidor, tentativas, motivo);
                Registrar(span, TelemetriaOms.Resultados.Dlq);
            }

            span.MarcarErro(excecao.GetType().Name);
        }

        await sessao.CanalConsumo.BasicAckAsync(entrega.DeliveryTag, multiple: false, cancellationToken);
    }

    /// <summary>O resultado no span de entrega e na métrica de mensagens (REQ-16, contrato › Métricas).</summary>
    private void Registrar(Activity? span, string resultado)
    {
        span?.SetTag(TelemetriaOms.Atributos.Resultado, resultado);
        TelemetriaOms.Mensagens.Add(1, new(TelemetriaOms.Atributos.Consumidor, consumidor), new(TelemetriaOms.Atributos.Resultado, resultado));
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

    /// <summary>Republica com confirmação pelo canal da sessão que entregou; só depois a original recebe ack.</summary>
    private async Task PublicarAsync(
        SessaoConsumo sessao, string fila, byte[] corpo, IReadOnlyBasicProperties original, Dictionary<string, object?> cabecalhos, CancellationToken cancellationToken)
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
        await sessao.CanalPublicacao.BasicPublishAsync("", fila, mandatory: false, propriedades, corpo, cancellationToken);
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
}
