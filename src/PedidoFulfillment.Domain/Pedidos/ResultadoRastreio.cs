namespace PedidoFulfillment.Domain.Pedidos;

/// <summary>O que o registro de rastreio fez com o pedido (REQ-11, research R-05).</summary>
public enum ResultadoRastreio
{
    /// <summary>ProntoParaEnvio → Despachado, com o rastreio gravado.</summary>
    Despachado,

    /// <summary>Pedido já despachado; transportadora ou código mudaram, sem mudar o status.</summary>
    Corrigido,

    /// <summary>Pedido já despachado com exatamente os mesmos dados; nada mudou.</summary>
    SemMudanca
}
