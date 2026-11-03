namespace PedidoFulfillment.Application.Produtos;

/// <summary>Filtro de situação da listagem de produtos, exclusivo da operação (REQ-22, FR-004).</summary>
public enum SituacaoProduto
{
    Todos,
    Ativos,
    Inativos
}
