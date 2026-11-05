namespace PedidoFulfillment.Application.Emails;

/// <summary>
/// Grava o pedido de envio, sem token nem corpo (REQ-24, R-03). Usa o contexto da requisição: com uma
/// transação aberta, a gravação entra nela, junto com a conta ou a senha que a originou.
/// </summary>
public interface IFilaEmails
{
    Task EnfileirarAsync(TipoEmail tipo, string destino, CancellationToken cancellationToken);
}
