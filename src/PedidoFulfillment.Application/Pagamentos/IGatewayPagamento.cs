namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Cobrança no serviço de pagamento (REQ-09, research R-02). Hoje é simulada; um gateway real
/// substitui a implementação sem mudar o consumidor.
/// </summary>
public interface IGatewayPagamento
{
    Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken);
}

/// <summary>Resultado da cobrança: identificador da transação na aprovação, motivo na recusa.</summary>
public sealed record ResultadoCobranca(bool Aprovado, string? IdentificadorTransacao, string? Motivo)
{
    public static ResultadoCobranca Aprovada(string identificadorTransacao) => new(true, identificadorTransacao, null);

    public static ResultadoCobranca Recusada(string motivo) => new(false, null, motivo);
}
