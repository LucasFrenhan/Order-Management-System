namespace PedidoFulfillment.Domain.Pagamentos;

/// <summary>
/// Situação do pagamento de um pedido. Gravada como texto (data-model).
/// </summary>
/// <remarks>
/// A numeração começa em 1 para que um valor ausente (0) seja detectado como inválido em vez
/// de virar <see cref="Pendente"/> silenciosamente.
/// </remarks>
public enum SituacaoPagamento
{
    Pendente = 1,
    Aprovado,
    Recusado,

    /// <summary>
    /// A cobrança se esgotou na reconciliação sem resposta válida do gateway (REQ-12, FR-012).
    /// Situação interna: não aparece na API nem nos eventos.
    /// </summary>
    NaoProcessado
}
