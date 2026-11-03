using System.ComponentModel.DataAnnotations;
using System.Globalization;
using PedidoFulfillment.Application.Produtos;

namespace PedidoFulfillment.Application.Contratos.Produtos;

/// <summary>
/// Parâmetros de <c>GET /api/v1/produtos</c> (REQ-22), como texto, para o 400 trazer todos os
/// erros de uma vez, como a listagem de pedidos (REQ-21, R-05). O cursor é conferido no serviço.
/// </summary>
public sealed record ListarProdutosRequestDTO : IValidatableObject
{
    public const int TamanhoPadrao = 20;
    public const int TamanhoMaximo = 100;
    private const int TamanhoMinimoBusca = 2;
    private const int TamanhoMaximoBusca = 100;

    public string? Busca { get; init; }

    public string? Situacao { get; init; }

    public string? Tamanho { get; init; }

    public string? Cursor { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Busca is not null && !BuscaValida(Busca))
        {
            yield return new ValidationResult(MensagensCatalogo.Busca, ["busca"]);
        }

        if (Situacao is not null && !TentarSituacao(Situacao, out _))
        {
            yield return new ValidationResult(MensagensCatalogo.Situacao, ["situacao"]);
        }

        if (Tamanho is not null && !TentarTamanho(Tamanho, out _))
        {
            yield return new ValidationResult($"O tamanho deve ser um número entre 1 e {TamanhoMaximo}.", ["tamanho"]);
        }
    }

    /// <summary>Palavras da busca; vazia sem busca. Só depois da validação.</summary>
    public IReadOnlyList<string> Palavras() => FiltroListagemProdutos.ExtrairPalavras(Busca);

    /// <summary>Situação pedida, ou nula; só depois da validação.</summary>
    public SituacaoProduto? SituacaoPedida() => Situacao is null ? null
        : TentarSituacao(Situacao, out var situacao) ? situacao : throw Invalido(nameof(Situacao));

    /// <summary>Tamanho da página; o padrão quando ausente. Só depois da validação.</summary>
    public int TamanhoPagina() => Tamanho is null ? TamanhoPadrao
        : TentarTamanho(Tamanho, out var tamanho) ? tamanho : throw Invalido(nameof(Tamanho));

    private static bool BuscaValida(string busca)
    {
        var aparada = busca.Trim();
        var palavras = FiltroListagemProdutos.ExtrairPalavras(aparada).Count;
        return aparada.Length is >= TamanhoMinimoBusca and <= TamanhoMaximoBusca
            && palavras is >= 1 and <= FiltroListagemProdutos.MaximoPalavras;
    }

    private static bool TentarSituacao(string texto, out SituacaoProduto situacao) =>
        Enum.TryParse(texto, ignoreCase: true, out situacao) && Enum.IsDefined(situacao) && !int.TryParse(texto, out _);

    private static bool TentarTamanho(string texto, out int tamanho) =>
        int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out tamanho) && tamanho is >= 1 and <= TamanhoMaximo;

    private static InvalidOperationException Invalido(string parametro) =>
        new($"O parâmetro {parametro} não foi validado antes da conversão.");
}
