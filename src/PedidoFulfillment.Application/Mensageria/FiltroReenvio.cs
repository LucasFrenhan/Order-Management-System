namespace PedidoFulfillment.Application.Mensageria;

/// <summary>
/// Quais mensagens da DLQ um pedido de reenvio seleciona (REQ-14, R-05). Campos nulos não
/// filtram; os informados se combinam.
/// </summary>
public sealed record FiltroReenvio(string? TipoEvento, DateTimeOffset? FalhouDe, DateTimeOffset? FalhouAte, string? TrechoMotivo);
