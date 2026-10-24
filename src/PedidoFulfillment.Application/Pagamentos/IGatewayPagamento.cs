namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Cobrança e estorno no serviço de pagamento (REQ-09, research R-02; REQ-13, R-03). Hoje é
/// simulado; um gateway real substitui a implementação sem mudar os chamadores.
/// </summary>
/// <remarks>
/// O pedido é a chave de idempotência das duas operações: repetir a chamada para o mesmo pedido
/// devolve o mesmo resultado, sem cobrar nem estornar de novo (REQ-13, FR-013 e FR-014). Uma
/// exceção é falha técnica, e o chamador tenta de novo.
/// </remarks>
public interface IGatewayPagamento
{
    Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken);

    /// <summary>Estorna o valor cobrado na transação <paramref name="identificadorTransacao"/> (REQ-13, FR-002).</summary>
    Task<ResultadoEstorno> EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken cancellationToken);
}

/// <summary>Resultado da cobrança: identificador da transação na aprovação, motivo na recusa.</summary>
public sealed record ResultadoCobranca(bool Aprovado, string? IdentificadorTransacao, string? Motivo)
{
    public static ResultadoCobranca Aprovada(string identificadorTransacao) => new(true, identificadorTransacao, null);

    public static ResultadoCobranca Recusada(string motivo) => new(false, null, motivo);
}

/// <summary>Resultado do estorno: identificador do estorno na confirmação, motivo na recusa (REQ-13, R-03).</summary>
public sealed record ResultadoEstorno(bool Confirmado, string? IdentificadorEstorno, string? Motivo)
{
    public static ResultadoEstorno Confirmar(string identificadorEstorno) => new(true, identificadorEstorno, null);

    public static ResultadoEstorno Recusar(string motivo) => new(false, null, motivo);
}
