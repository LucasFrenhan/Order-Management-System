namespace PedidoFulfillment.Application.Produtos;

/// <summary>Outro cadastro gravou o mesmo SKU antes (índice único, REQ-22, research R-08).</summary>
public sealed class SkuDuplicadoException(Exception? excecaoInterna = null)
    : Exception("Já existe um produto com este SKU.", excecaoInterna);
