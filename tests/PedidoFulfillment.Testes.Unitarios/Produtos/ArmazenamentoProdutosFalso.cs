using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Application.Produtos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Produtos;

/// <summary>
/// Catálogo em memória (REQ-22). Cada leitura para alterar devolve uma cópia, e só a gravação
/// bem-sucedida a guarda; assim um conflito descartado não deixa alteração para trás. A busca é
/// por trecho, sem o Full-Text Search, que é coberto nos testes de integração.
/// </summary>
public sealed class ArmazenamentoProdutosFalso : IArmazenamentoProdutos
{
    private static readonly DateTimeOffset Instante = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<Guid, Produto> gravados = [];
    private readonly List<Produto> emAlteracao = [];

    public int Gravacoes { get; private set; }

    public int Leituras { get; private set; }

    public int Descartes { get; private set; }

    /// <summary>Quantas gravações seguidas recebem conflito de concorrência.</summary>
    public int ConflitosRestantes { get; set; }

    /// <summary>O que a "outra operação" faz no produto gravado a cada conflito.</summary>
    public Action<Produto>? AoConflitar { get; set; }

    /// <summary>Na próxima gravação, outro cadastro gravou o mesmo SKU antes.</summary>
    public bool SkuDuplicadoNaProximaGravacao { get; set; }

    public IReadOnlyCollection<Produto> Produtos => gravados.Values;

    public List<(FiltroListagemProdutos Filtro, PosicaoCursorProdutos? Posicao, int Quantidade)> Listagens { get; } = [];

    public Produto Guardar(string sku, string nome, decimal preco = 10m, int estoque = 5, bool ativo = true, string? descricao = null)
    {
        var produto = new Produto(sku, nome, descricao, preco, estoque, ativo, Instante);
        gravados[produto.Id] = produto;
        return produto;
    }

    public Produto Gravado(Guid id) => gravados[id];

    public Task<Produto?> ObterAsync(Guid id, bool paraAlterar, CancellationToken cancellationToken)
    {
        Leituras++;
        if (!gravados.TryGetValue(id, out var produto))
        {
            return Task.FromResult<Produto?>(null);
        }

        if (!paraAlterar)
        {
            return Task.FromResult<Produto?>(produto);
        }

        var copia = Copiar(produto);
        emAlteracao.Add(copia);
        return Task.FromResult<Produto?>(copia);
    }

    public Task<bool> SkuEmUsoAsync(string sku, CancellationToken cancellationToken) =>
        Task.FromResult(gravados.Values.Any(p => string.Equals(p.Sku, sku, StringComparison.OrdinalIgnoreCase)));

    public void Adicionar(Produto produto) => emAlteracao.Add(produto);

    public Task<IReadOnlyList<Produto>> ListarAsync(FiltroListagemProdutos filtro, PosicaoCursorProdutos? posicao, int quantidade, CancellationToken cancellationToken)
    {
        Listagens.Add((filtro, posicao, quantidade));
        var filtrados = gravados.Values
            .Where(p => filtro.Situacao switch { SituacaoProduto.Ativos => p.Ativo, SituacaoProduto.Inativos => !p.Ativo, _ => true })
            .Where(p => filtro.Palavras.All(palavra => $"{p.Nome} {p.Sku} {p.Descricao}".Contains(palavra, StringComparison.OrdinalIgnoreCase)));

        static int Comparar(Produto p, PosicaoCursorProdutos posicao)
        {
            var porNome = string.Compare(p.Nome, posicao.Nome, StringComparison.OrdinalIgnoreCase);
            return porNome != 0 ? porNome : string.Compare(p.Sku, posicao.Sku, StringComparison.OrdinalIgnoreCase);
        }

        List<Produto> lidos;
        if (posicao is { Sentido: SentidoCursor.Anterior })
        {
            lidos = [.. filtrados.Where(p => posicao.Inclusiva ? Comparar(p, posicao) <= 0 : Comparar(p, posicao) < 0)
                .OrderByDescending(p => p.Nome, StringComparer.OrdinalIgnoreCase).ThenByDescending(p => p.Sku, StringComparer.OrdinalIgnoreCase)
                .Take(quantidade)];
            lidos.Reverse();
        }
        else
        {
            lidos = [.. filtrados.Where(p => posicao is null || (posicao.Inclusiva ? Comparar(p, posicao) >= 0 : Comparar(p, posicao) > 0))
                .OrderBy(p => p.Nome, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Sku, StringComparer.OrdinalIgnoreCase)
                .Take(quantidade)];
        }

        return Task.FromResult<IReadOnlyList<Produto>>(lidos);
    }

    /// <summary>Como o <c>UPDATE</c> atômico: soma sobre o valor gravado, se couber nos limites.</summary>
    public Task<ResultadoAjusteEstoque> AjustarEstoqueAsync(Guid id, int variacao, int maximo, DateTimeOffset instante, CancellationToken cancellationToken)
    {
        if (!gravados.TryGetValue(id, out var produto))
        {
            return Task.FromResult(ResultadoAjusteEstoque.Inexistente);
        }

        var nova = (long)produto.QuantidadeEstoque + variacao;
        if (nova < 0)
        {
            return Task.FromResult(ResultadoAjusteEstoque.Insuficiente);
        }

        if (nova > maximo)
        {
            return Task.FromResult(ResultadoAjusteEstoque.AcimaDoLimite);
        }

        produto.AjustarEstoque(variacao, instante);
        Gravacoes++;
        return Task.FromResult(ResultadoAjusteEstoque.Ajustado);
    }

    public Task SalvarAsync(CancellationToken cancellationToken)
    {
        if (SkuDuplicadoNaProximaGravacao)
        {
            SkuDuplicadoNaProximaGravacao = false;
            throw new SkuDuplicadoException();
        }

        if (ConflitosRestantes > 0)
        {
            ConflitosRestantes--;
            foreach (var produto in emAlteracao.Where(p => gravados.ContainsKey(p.Id)))
            {
                var outro = Copiar(gravados[produto.Id]);
                AoConflitar?.Invoke(outro);
                gravados[produto.Id] = outro;
            }

            throw new ConflitoConcorrenciaException(nameof(Produto), "teste");
        }

        foreach (var produto in emAlteracao)
        {
            gravados[produto.Id] = produto;
        }

        emAlteracao.Clear();
        Gravacoes++;
        return Task.CompletedTask;
    }

    public void DescartarAlteracoes()
    {
        emAlteracao.Clear();
        Descartes++;
    }

    private static Produto Copiar(Produto p) =>
        new(p.Sku, p.Nome, p.Descricao, p.Preco, p.QuantidadeEstoque, p.Ativo, p.CriadoEm.ToUniversalTime(), p.Id);
}
