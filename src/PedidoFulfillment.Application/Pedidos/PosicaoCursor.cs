namespace PedidoFulfillment.Application.Pedidos;

/// <summary>Para que lado a listagem anda a partir de uma posição (REQ-21, research R-02).</summary>
public enum SentidoCursor
{
    Proxima,
    Anterior
}

/// <summary>
/// Posição na ordem da listagem, <c>CriadoEm DESC, Numero DESC</c> (research R-01), e o sentido
/// da navegação a partir dela. Sem <see cref="Inclusiva"/>, a página começa estritamente depois
/// (ou antes) da posição. A posição inclusiva só aparece no cursor de volta de uma página vazia,
/// para a volta trazer também o pedido que estava na posição (research R-02).
/// </summary>
public sealed record PosicaoCursor(SentidoCursor Sentido, DateTimeOffset CriadoEm, string Numero, bool Inclusiva = false);
