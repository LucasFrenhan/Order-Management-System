namespace PedidoFulfillment.Application.Produtos;

/// <summary>Resultado do ajuste atômico de estoque (REQ-22, research R-07).</summary>
public enum ResultadoAjusteEstoque
{
    Ajustado,
    Inexistente,

    /// <summary>O estoque ficaria negativo; nada foi gravado.</summary>
    Insuficiente,

    /// <summary>O estoque passaria do máximo; nada foi gravado.</summary>
    AcimaDoLimite
}
