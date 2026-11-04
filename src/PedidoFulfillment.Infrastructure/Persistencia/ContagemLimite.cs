namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// Uma contagem do limite de requisições (REQ-23, data-model). É um estado técnico e descartável:
/// perder uma linha só devolve tentativas até o fim da janela. Por isso fica fora do domínio.
/// </summary>
internal sealed class ContagemLimite(byte[] chave, int contador, DateTime fimJanela)
{
    public const int TamanhoChave = 32;

    /// <summary>HMAC da política, da origem e do e-mail, sem dados legíveis (FR-010).</summary>
    public byte[] Chave { get; private set; } = chave;

    public int Contador { get; private set; } = contador;

    /// <summary>Em UTC, pelo relógio do banco (FR-008).</summary>
    public DateTime FimJanela { get; private set; } = fimJanela;

    /// <summary>A primeira recusa da janela já gerou o log (FR-020).</summary>
    public bool BloqueioRegistrado { get; private set; }
}
