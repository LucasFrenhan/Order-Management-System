using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Notificacoes;

/// <summary>Uma conexão SSE pedida: os eventos, ou nada se o usuário já está no limite (FR-023).</summary>
public sealed record AberturaEventos(IAsyncEnumerable<SseItem<string>>? Eventos, int Limite)
{
    public bool LimiteAtingido => Eventos is null;
}

/// <summary>
/// O fluxo de uma conexão SSE (REQ-18, FR-019 a FR-022, research R-16): registra a conexão antes
/// de ler o ponto de partida, manda <c>conectado</c> ou o reenvio a partir do <c>Last-Event-ID</c>,
/// depois os avisos ao vivo, intercalando o <c>sinal</c>, e termina no <c>exp</c> do token. Fica
/// aqui, e não no endpoint, que só roteia (constituição; analyze C1).
/// </summary>
public sealed partial class FluxoEventosTempoReal
{
    public const string EventoConectado = "conectado";
    public const string EventoSincronizacao = "sincronizacao-necessaria";
    public const string EventoSinal = "sinal";

    /// <summary>"Antes do primeiro aviso": o <c>id</c> do <c>conectado</c> com a tabela vazia.</summary>
    public static readonly string IdInicio = Guid.Empty.ToString();

    /// <summary>Teto da duração, para um token sem <c>exp</c> (o token de acesso dura 15 minutos por padrão, REQ-24).</summary>
    private static readonly TimeSpan MaximoConexao = TimeSpan.FromDays(1);

    private readonly LeituraAvisos leitura;
    private readonly DistribuidorAvisos distribuidor;
    private readonly IOptions<OpcoesNotificacoes> opcoes;
    private readonly TimeProvider relogio;
    private readonly ILogger<FluxoEventosTempoReal> logger;

    internal FluxoEventosTempoReal(
        LeituraAvisos leitura, DistribuidorAvisos distribuidor, IOptions<OpcoesNotificacoes> opcoes, TimeProvider relogio, ILogger<FluxoEventosTempoReal> logger)
    {
        this.leitura = leitura;
        this.distribuidor = distribuidor;
        this.opcoes = opcoes;
        this.relogio = relogio;
        this.logger = logger;
    }

    private enum Motivo
    {
        AvisoDesconhecido,
        LimiteReenvio,
        ClienteLento
    }

    /// <param name="ultimoEvento">O cabeçalho <c>Last-Event-ID</c>, se veio.</param>
    /// <param name="expiraEm">O <c>exp</c> do token: a conexão termina nele.</param>
    public AberturaEventos Abrir(Guid usuarioId, bool operacao, Guid? pedidoId, string? ultimoEvento, DateTimeOffset expiraEm)
    {
        var conexao = distribuidor.Registrar(usuarioId, operacao, pedidoId);
        if (conexao is null)
        {
            EventosNotificacao.ConexaoRecusada(logger, usuarioId, distribuidor.Limite);
            return new AberturaEventos(null, distribuidor.Limite);
        }

        EventosNotificacao.ConexaoAberta(logger, usuarioId, pedidoId, ultimoEvento is not null);
        return new AberturaEventos(EventosAsync(conexao, ultimoEvento, expiraEm, CancellationToken.None), distribuidor.Limite);
    }

    private async IAsyncEnumerable<SseItem<string>> EventosAsync(
        ConexaoEventos conexao, string? ultimoEvento, DateTimeOffset expiraEm, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var motivoFim = "cliente desconectou";
        using var fim = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var restante = expiraEm - relogio.GetUtcNow();
        fim.CancelAfter(restante <= TimeSpan.Zero ? TimeSpan.Zero : restante < MaximoConexao ? restante : MaximoConexao);
        try
        {
            var (inicio, enviada) = await PontoDePartidaAsync(conexao, ultimoEvento, fim.Token);
            foreach (var item in inicio)
            {
                yield return item;
            }

            while (true)
            {
                var (aviso, tipo) = await ProximoAsync(conexao, fim.Token);
                if (tipo == Proximo.Fim)
                {
                    motivoFim = conexao.Lenta ? "fila cheia" : fim.IsCancellationRequested && !cancellationToken.IsCancellationRequested ? "token expirado" : motivoFim;
                    if (conexao.Lenta)
                    {
                        yield return Sincronizacao(Motivo.ClienteLento);
                    }

                    yield break;
                }

                if (tipo == Proximo.Sinal)
                {
                    yield return new SseItem<string>("{}", EventoSinal);
                    continue;
                }

                // O que veio antes do ponto de partida já foi mandado (ou é anterior ao conectado).
                if (aviso!.Posicao <= enviada)
                {
                    continue;
                }

                enviada = aviso.Posicao;
                yield return Aviso(aviso);
            }
        }
        finally
        {
            distribuidor.Remover(conexao);
            EventosNotificacao.ConexaoEncerrada(logger, conexao.UsuarioId, motivoFim);
        }
    }

    /// <summary>
    /// Sem <c>Last-Event-ID</c>, o <c>conectado</c> com o último aviso confirmado (FR-021a); com ele,
    /// os avisos perdidos, até o máximo, ou <c>sincronizacao-necessaria</c> (FR-021).
    /// </summary>
    private async Task<(List<SseItem<string>> Itens, ulong Enviada)> PontoDePartidaAsync(
        ConexaoEventos conexao, string? ultimoEvento, CancellationToken cancellationToken)
    {
        var ultima = await leitura.UltimaConfirmadaAsync(cancellationToken);
        var ultimaPosicao = ultima?.Posicao ?? 0;
        if (ultimoEvento is null)
        {
            return ([new SseItem<string>(Json(new { instante = relogio.GetUtcNow() }), EventoConectado) { EventId = ultima?.IdNotificacao.ToString() ?? IdInicio }], ultimaPosicao);
        }

        ulong? desde = !Guid.TryParse(ultimoEvento, out var id) ? null : id == Guid.Empty ? 0 : await leitura.PosicaoDeAsync(id, cancellationToken);
        if (desde is not { } posicao)
        {
            return ([Sincronizacao(Motivo.AvisoDesconhecido)], ultimaPosicao);
        }

        var maximo = opcoes.Value.Sse.MaximoReenvio;
        var perdidos = conexao.PedidoId is { } pedidoId ? await leitura.LerDoPedidoAsync(pedidoId, posicao, maximo + 1, cancellationToken)
            : conexao.Operacao ? await leitura.LerAsync(posicao, maximo + 1, cancellationToken)
            : await leitura.LerDoClienteAsync(conexao.UsuarioId, posicao, maximo + 1, cancellationToken);
        if (perdidos.Count > maximo)
        {
            return ([Sincronizacao(Motivo.LimiteReenvio)], ultimaPosicao);
        }

        return ([.. perdidos.Select(Aviso)], perdidos.Count > 0 ? perdidos[^1].Posicao : posicao);
    }

    private enum Proximo
    {
        Aviso,
        Sinal,
        Fim
    }

    /// <summary>O próximo aviso da fila, um sinal depois do intervalo sem aviso, ou o fim.</summary>
    private async Task<(AvisoLido? Aviso, Proximo Tipo)> ProximoAsync(ConexaoEventos conexao, CancellationToken cancellationToken)
    {
        if (conexao.Fila.Reader.TryRead(out var pronto))
        {
            return (pronto, Proximo.Aviso);
        }

        using var espera = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        espera.CancelAfter(opcoes.Value.Sse.SinalDeVida);
        try
        {
            if (!await conexao.Fila.Reader.WaitToReadAsync(espera.Token))
            {
                return (null, Proximo.Fim);
            }

            return conexao.Fila.Reader.TryRead(out var aviso) ? (aviso, Proximo.Aviso) : (null, Proximo.Sinal);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, Proximo.Sinal);
        }
        catch (OperationCanceledException)
        {
            return (null, Proximo.Fim);
        }
    }

    private static SseItem<string> Aviso(AvisoLido aviso) => new(aviso.Conteudo, aviso.Tipo) { EventId = aviso.IdNotificacao.ToString() };

    private static SseItem<string> Sincronizacao(Motivo motivo) => new(Json(new { motivo = motivo.ToString() }), EventoSincronizacao);

    private static string Json(object valor) => JsonSerializer.Serialize(valor);
}
