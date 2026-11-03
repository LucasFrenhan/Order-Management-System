using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Application.Produtos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// <see cref="IArmazenamentoProdutos"/> sobre o <see cref="ContextoBancoDados"/> (REQ-22). O conflito
/// de concorrência já chega traduzido pelo contexto.
/// </summary>
internal sealed class ArmazenamentoProdutos(ContextoBancoDados contexto) : IArmazenamentoProdutos
{
    /// <summary>Índice único do SKU, sem diferenciar maiúsculas (REQ-04, R-06).</summary>
    private const string IndiceSku = "IX_Produtos_Sku";

    /// <summary>Português do Brasil no separador de palavras do Full-Text Search (research R-01).</summary>
    private const int IdiomaBusca = 1046;

    public Task<Produto?> ObterAsync(Guid id, bool paraAlterar, CancellationToken cancellationToken)
    {
        var produtos = paraAlterar ? contexto.Produtos : contexto.Produtos.AsNoTracking();
        return produtos.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    // A collation do SKU não diferencia maiúsculas (ConfiguracaoProduto).
    public Task<bool> SkuEmUsoAsync(string sku, CancellationToken cancellationToken) =>
        contexto.Produtos.AnyAsync(p => p.Sku == sku, cancellationToken);

    public void Adicionar(Produto produto) => contexto.Produtos.Add(produto);

    /// <summary>
    /// Paginação por chave sobre <c>(Nome, Sku)</c> nos dois sentidos (research R-05), com a busca
    /// pelo índice full-text e a condição como parâmetro (R-04).
    /// </summary>
    public async Task<IReadOnlyList<Produto>> ListarAsync(
        FiltroListagemProdutos filtro, PosicaoCursorProdutos? posicao, int quantidade, CancellationToken cancellationToken)
    {
        var produtos = contexto.Produtos.AsNoTracking();
        produtos = filtro.Situacao switch
        {
            SituacaoProduto.Ativos => produtos.Where(p => p.Ativo),
            SituacaoProduto.Inativos => produtos.Where(p => !p.Ativo),
            _ => produtos
        };

        if (filtro.Palavras.Count > 0)
        {
            var condicao = filtro.CondicaoFullText();
            produtos = produtos.Where(p => EF.Functions.Contains(EF.Property<string>(p, InterceptorTextoBusca.Propriedade), condicao, IdiomaBusca));
        }

        // A ordem da listagem é crescente (Nome, Sku): "depois" é maior e "antes" é menor.
        List<Produto> lidos;
        if (posicao is { Sentido: SentidoCursor.Anterior } anterior)
        {
            var (n, k) = (anterior.Nome, anterior.Sku);
            produtos = anterior.Inclusiva
                ? produtos.Where(p => string.Compare(p.Nome, n) < 0 || (p.Nome == n && string.Compare(p.Sku, k) <= 0))
                : produtos.Where(p => string.Compare(p.Nome, n) < 0 || (p.Nome == n && string.Compare(p.Sku, k) < 0));
            lidos = await produtos.OrderByDescending(p => p.Nome).ThenByDescending(p => p.Sku).Take(quantidade).ToListAsync(cancellationToken);
            lidos.Reverse();
        }
        else
        {
            if (posicao is { } proxima)
            {
                var (n, k) = (proxima.Nome, proxima.Sku);
                produtos = proxima.Inclusiva
                    ? produtos.Where(p => string.Compare(p.Nome, n) > 0 || (p.Nome == n && string.Compare(p.Sku, k) >= 0))
                    : produtos.Where(p => string.Compare(p.Nome, n) > 0 || (p.Nome == n && string.Compare(p.Sku, k) > 0));
            }

            lidos = await produtos.OrderBy(p => p.Nome).ThenBy(p => p.Sku).Take(quantidade).ToListAsync(cancellationToken);
        }

        return lidos;
    }

    public async Task SalvarAsync(CancellationToken cancellationToken)
    {
        try
        {
            await contexto.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (ErrosBancoDados.EhViolacaoUnicidade(excecao, IndiceSku))
        {
            throw new SkuDuplicadoException(excecao);
        }
    }

    public void DescartarAlteracoes() => contexto.ChangeTracker.Clear();
}
