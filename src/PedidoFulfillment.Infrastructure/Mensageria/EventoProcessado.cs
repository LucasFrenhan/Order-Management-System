namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Evento já processado com sucesso por um consumidor (REQ-06, R-08). Gravado na mesma
/// transação do efeito; a chave (consumidor, evento) descarta reentregas (FR-015).
/// </summary>
internal sealed class EventoProcessado(string consumidor, Guid idEvento, DateTimeOffset processadoEm)
{
    public const int TamanhoMaximoConsumidor = 100;

    public string Consumidor { get; private set; } = consumidor;

    public Guid IdEvento { get; private set; } = idEvento;

    public DateTimeOffset ProcessadoEm { get; private set; } = processadoEm;
}
