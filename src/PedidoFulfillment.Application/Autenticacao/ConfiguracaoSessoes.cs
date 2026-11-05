namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>
/// Validades das sessões, montadas pela Infrastructure a partir das opções validadas na partida: a
/// Application não depende de <c>Options</c> (REQ-24, FR-005).
/// </summary>
public sealed record ConfiguracaoSessoes(TimeSpan ValidadeToken, TimeSpan DuracaoMaxima);
