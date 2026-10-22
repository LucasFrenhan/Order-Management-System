using PedidoFulfillment.Domain.Comum;

namespace PedidoFulfillment.Domain.Produtos;

/// <summary>
/// Item vendável do catálogo (data-model). A versão de concorrência otimista existe só no
/// mapeamento (research R-04).
/// </summary>
public sealed class Produto
{
    public const int TamanhoMaximoSku = 50;
    public const int TamanhoMaximoNome = 200;
    public const int TamanhoMaximoDescricao = 1000;

    public Produto(string sku, string nome, string? descricao, decimal preco, int quantidadeEstoque, bool ativo, DateTimeOffset instante, Guid? id = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quantidadeEstoque);
        Id = id ?? Guid.CreateVersion7();
        Sku = Guardas.Texto(sku, TamanhoMaximoSku);
        Nome = Guardas.Texto(nome, TamanhoMaximoNome);
        Descricao = Guardas.TextoOpcional(descricao, TamanhoMaximoDescricao);
        Preco = Guardas.Dinheiro(preco);
        QuantidadeEstoque = quantidadeEstoque;
        Ativo = ativo;
        CriadoEm = Guardas.Utc(instante);
        AtualizadoEm = CriadoEm;
    }

    private Produto()
    {
        Sku = "";
        Nome = "";
    }

    public Guid Id { get; private set; }

    public string Sku { get; private set; }

    public string Nome { get; private set; }

    public string? Descricao { get; private set; }

    public decimal Preco { get; private set; }

    public int QuantidadeEstoque { get; private set; }

    public bool Ativo { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public DateTimeOffset AtualizadoEm { get; private set; }

    /// <summary>Soma <paramref name="variacao"/> ao estoque; o resultado nunca fica negativo.</summary>
    public void AjustarEstoque(int variacao, DateTimeOffset instante)
    {
        Guardas.Utc(instante);
        var novaQuantidade = QuantidadeEstoque + variacao;
        if (novaQuantidade < 0)
        {
            throw new InvalidOperationException($"O estoque do produto {Sku} não pode ficar negativo.");
        }

        QuantidadeEstoque = novaQuantidade;
        AtualizadoEm = instante;
    }

    public void AlterarDados(string nome, string? descricao, decimal preco, DateTimeOffset instante)
    {
        var novoNome = Guardas.Texto(nome, TamanhoMaximoNome);
        var novaDescricao = Guardas.TextoOpcional(descricao, TamanhoMaximoDescricao);
        var novoPreco = Guardas.Dinheiro(preco);
        AtualizadoEm = Guardas.Utc(instante);
        Nome = novoNome;
        Descricao = novaDescricao;
        Preco = novoPreco;
    }

    public void Ativar(DateTimeOffset instante)
    {
        AtualizadoEm = Guardas.Utc(instante);
        Ativo = true;
    }

    public void Desativar(DateTimeOffset instante)
    {
        AtualizadoEm = Guardas.Utc(instante);
        Ativo = false;
    }
}
