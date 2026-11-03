using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Application.Produtos;

/// <summary>Persistência do catálogo de produtos (REQ-22, research R-03 a R-08).</summary>
public interface IArmazenamentoProdutos
{
    Task<Produto?> ObterAsync(Guid id, bool paraAlterar, CancellationToken cancellationToken);

    Task<bool> SkuEmUsoAsync(string sku, CancellationToken cancellationToken);

    void Adicionar(Produto produto);

    /// <summary>
    /// Produtos em ordem <c>Nome ASC, Sku ASC</c>. Sem posição, do começo; com
    /// <c>Proxima</c>, os depois da posição; com <c>Anterior</c>, os antes, já na ordem da
    /// listagem. A busca usa o índice full-text. No máximo <paramref name="quantidade"/> itens.
    /// </summary>
    Task<IReadOnlyList<Produto>> ListarAsync(FiltroListagemProdutos filtro, PosicaoCursorProdutos? posicao, int quantidade, CancellationToken cancellationToken);

    /// <exception cref="SkuDuplicadoException">Outro cadastro gravou o mesmo SKU.</exception>
    /// <exception cref="Comum.ConflitoConcorrenciaException">O produto mudou desde a leitura.</exception>
    Task SalvarAsync(CancellationToken cancellationToken);

    void DescartarAlteracoes();
}
