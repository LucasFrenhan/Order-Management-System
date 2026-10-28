using System.Collections.Concurrent;

namespace PedidoFulfillment.Infrastructure.Observabilidade;

/// <summary>
/// Última leitura das métricas de estado (REQ-16, R-07): os gauges observáveis são lidos pelo
/// exportador de forma síncrona e não podem ir ao banco nem ao broker (FR-017). O
/// <see cref="AmostradorOutbox"/> e o <c>MonitorDlq</c> gravam aqui; os gauges só leem. Por
/// processo: com várias instâncias, cada uma publica a sua, e o painel usa o maior valor (FR-016a).
/// </summary>
internal static class EstadoMetricas
{
    private static readonly ConcurrentDictionary<string, long> Dlq = new();
    private static readonly ConcurrentDictionary<string, long> Inscricoes = new();
    private static LeituraOutbox? outbox;

    /// <summary>Pendentes no outbox e idade do mais antigo; sem leitura ainda, nulo.</summary>
    public static LeituraOutbox? Outbox => Volatile.Read(ref outbox);

    public static IEnumerable<KeyValuePair<string, long>> MensagensDlq => Dlq;

    /// <summary>1 inscrito, 0 sem inscrição, por consumidor (REQ-17, métrica 7).</summary>
    public static IEnumerable<KeyValuePair<string, long>> ConsumidoresInscritos => Inscricoes;

    public static void RegistrarInscricao(string consumidor, bool inscrito) => Inscricoes[consumidor] = inscrito ? 1 : 0;

    public static void RegistrarOutbox(long pendentes, double idadeSegundos) =>
        Volatile.Write(ref outbox, new LeituraOutbox(pendentes, idadeSegundos));

    public static void RegistrarDlq(string consumidor, long mensagens) => Dlq[consumidor] = mensagens;
}

internal sealed record LeituraOutbox(long Pendentes, double IdadeSegundos);
