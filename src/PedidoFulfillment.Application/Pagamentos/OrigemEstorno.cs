namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>Quem pediu o estorno (REQ-13, R-11), para o log 7005.</summary>
public enum OrigemEstorno
{
    /// <summary>Cancelamento pela API, por Admin ou OperadorLogistica.</summary>
    Operacao = 1,

    /// <summary>Esgotamento da etapa de entrega na reconciliação.</summary>
    Reconciliacao,

    /// <summary>Cobrança aprovada para um pedido cancelado antes da gravação (R-07).</summary>
    CobrancaAposCancelamento
}
