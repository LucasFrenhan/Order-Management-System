namespace PedidoFulfillment.Application.Notificacoes;

/// <summary>
/// Espera entre as tentativas de entrega de um webhook (REQ-18, FR-015): 30 s, 2 min, 10 min,
/// 30 min, 1 h e depois a cada 2 h, até o limite contado da primeira tentativa. A última tentativa
/// cai no limite; uma falha nele ou depois esgota a entrega.
/// </summary>
public static class EsperaEntregas
{
    private static readonly TimeSpan[] Esperas =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1)
    ];

    private static readonly TimeSpan EsperaDepois = TimeSpan.FromHours(2);

    /// <summary>A espera depois da tentativa número <paramref name="tentativas"/> (a primeira é 1).</summary>
    public static TimeSpan Espera(int tentativas)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(tentativas, 1);
        return tentativas <= Esperas.Length ? Esperas[tentativas - 1] : EsperaDepois;
    }

    /// <summary>
    /// A próxima tentativa depois de uma falha, ou nulo se a entrega se esgotou: a falha aconteceu no
    /// limite ou depois dele.
    /// </summary>
    public static DateTimeOffset? Proxima(DateTimeOffset primeiraTentativa, int tentativas, DateTimeOffset agora, TimeSpan limite)
    {
        var fim = primeiraTentativa + limite;
        if (agora >= fim)
        {
            return null;
        }

        var proxima = agora + Espera(tentativas);
        return proxima < fim ? proxima : fim;
    }
}
