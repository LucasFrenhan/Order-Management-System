using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Persistencia;
using RabbitMQ.Client;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Publica os eventos pendentes do outbox no Worker (REQ-06, R-05): em ordem de registro, com
/// confirmação do broker, e só então os marca como enviados. Falha registra espera crescente e
/// encerra o lote, para não publicar um evento posterior antes do que falhou (FR-006, FR-007).
/// Entrega pelo menos uma vez: cair entre a confirmação e o commit republica o evento (FR-008).
/// </summary>
internal sealed partial class DespachanteEventosSaida(
    IServiceScopeFactory escopos,
    ConexaoRabbitMQ conexao,
    IOptions<OpcoesMensageria> opcoes,
    TimeProvider relogio,
    ILogger<DespachanteEventosSaida> logger) : BackgroundService
{
    // Com o broker sem responder, a confirmação nunca chega: o prazo devolve a falha ao laço.
    private static readonly TimeSpan PrazoPublicacao = TimeSpan.FromSeconds(5);

    private IChannel? canal;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var temporizador = new PeriodicTimer(TimeSpan.FromMilliseconds(opcoes.Value.IntervaloDespachoMs));
        try
        {
            do
            {
                try
                {
                    await DespacharLoteAsync(stoppingToken);
                }
                catch (Exception excecao) when (excecao is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    // Banco fora ou erro inesperado: tenta de novo no próximo ciclo, sem derrubar o processo.
                    FalhaDespacho(logger, excecao.GetType().Name, excecao.Message);
                    await DescartarCanalAsync();
                }
            }
            while (await temporizador.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento do Worker.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        await DescartarCanalAsync();
    }

    internal async Task DespacharLoteAsync(CancellationToken cancellationToken)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
        await using var transacao = await contexto.Database.BeginTransactionAsync(cancellationToken);

        // Sem compor com LINQ: composta, a SQL viraria subconsulta e as dicas de lock perderiam o
        // efeito (achado M4). UPDLOCK/READPAST deixa cada instância do Worker pegar linhas próprias.
        var pendentes = await contexto.EventosSaida
            .FromSqlRaw(
                "SELECT TOP ({0}) * FROM EventosSaida WITH (UPDLOCK, READPAST, ROWLOCK) WHERE EnviadoEm IS NULL ORDER BY Id",
                opcoes.Value.TamanhoLote)
            .ToListAsync(cancellationToken);

        foreach (var evento in pendentes)
        {
            var agora = relogio.GetUtcNow();
            if (evento.ProximaTentativaEm > agora)
            {
                // Ainda esperando: os seguintes ficam atrás dele, para manter a ordem.
                break;
            }

            if (!await PublicarAsync(evento, cancellationToken))
            {
                break;
            }
        }

        await contexto.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);
    }

    private async Task<bool> PublicarAsync(EventoSaida evento, CancellationToken cancellationToken)
    {
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        prazo.CancelAfter(PrazoPublicacao);
        try
        {
            var canalPublicacao = await ObterCanalAsync(prazo.Token);
            var propriedades = new BasicProperties
            {
                Persistent = true,
                MessageId = evento.IdEvento.ToString(),
                Type = evento.Tipo,
                ContentType = "application/json"
            };

            // Com confirmações rastreadas, só retorna depois do ack; nack ou falha de canal lançam.
            // mandatory: false — sem fila ligada, a mensagem é descartada sem erro (edge case da spec).
            await canalPublicacao.BasicPublishAsync(
                TopologiaMensageria.TrocaEventos, evento.Tipo, mandatory: false, propriedades, Encoding.UTF8.GetBytes(evento.Conteudo), prazo.Token);
            evento.MarcarEnviado(relogio.GetUtcNow());
            EventoPublicado(logger, evento.IdEvento, evento.Tipo);
            return true;
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            evento.RegistrarFalha($"{excecao.GetType().Name}: {excecao.Message}", relogio.GetUtcNow());
            FalhaPublicacao(logger, evento.IdEvento, evento.Tipo, evento.Tentativas, excecao.GetType().Name);
            await DescartarCanalAsync();
            return false;
        }
    }

    private async Task<IChannel> ObterCanalAsync(CancellationToken cancellationToken)
    {
        if (canal is { IsOpen: true })
        {
            return canal;
        }

        await DescartarCanalAsync();
        var conexaoAberta = await conexao.ObterAsync(cancellationToken);
        canal = await conexaoAberta.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await TopologiaMensageria.DeclararTrocaAsync(canal, cancellationToken);
        return canal;
    }

    private async Task DescartarCanalAsync()
    {
        if (canal is null)
        {
            return;
        }

        var antigo = canal;
        canal = null;
        try
        {
            using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await antigo.CloseAsync(200, "descartado", abort: true, prazo.Token);
        }
        catch (Exception)
        {
            // O canal já estava quebrado; basta liberá-lo.
        }

        antigo.Dispose();
    }

    [LoggerMessage(EventId = 5001, EventName = "EventoPublicado", Level = LogLevel.Information,
        Message = "Evento {IdEvento} ({Tipo}) publicado.")]
    private static partial void EventoPublicado(ILogger logger, Guid idEvento, string tipo);

    [LoggerMessage(EventId = 5002, EventName = "FalhaPublicacao", Level = LogLevel.Warning,
        Message = "Falha ao publicar o evento {IdEvento} ({Tipo}), tentativa {Tentativas}: {Erro}.")]
    private static partial void FalhaPublicacao(ILogger logger, Guid idEvento, string tipo, int tentativas, string erro);

    [LoggerMessage(EventId = 5007, EventName = "FalhaDespacho", Level = LogLevel.Warning,
        Message = "Falha no ciclo de despacho do outbox ({Erro}): {Mensagem}")]
    private static partial void FalhaDespacho(ILogger logger, string erro, string mensagem);
}
