using System.Text.RegularExpressions;

namespace PedidoFulfillment.Application.Produtos;

/// <summary>
/// Filtros da listagem de produtos já convertidos (REQ-22, data-model). Para o Cliente, a
/// situação é sempre <see cref="SituacaoProduto.Ativos"/> (FR-003).
/// </summary>
public sealed partial record FiltroListagemProdutos
{
    public const int MaximoPalavras = 10;

    /// <summary>Palavras da busca, em minúsculas e sem repetição; vazia = sem busca.</summary>
    public IReadOnlyList<string> Palavras { get; init; } = [];

    public SituacaoProduto Situacao { get; init; } = SituacaoProduto.Todos;

    /// <summary>
    /// Só sequências de letras e dígitos (research R-04): aspas, asteriscos, parênteses e
    /// operadores do Full-Text Search nunca chegam à condição como sintaxe.
    /// </summary>
    public static IReadOnlyList<string> ExtrairPalavras(string? busca) =>
        busca is null ? [] : [.. Palavra().Matches(busca).Select(m => m.Value.ToLowerInvariant()).Distinct()];

    /// <summary>
    /// Condição do <c>CONTAINS</c>: cada palavra nas formas flexionadas (plural e singular) ou como
    /// começo de palavra, e todas obrigatórias (research R-04). Vai como parâmetro da consulta.
    /// Números vão só por igualdade: o separador de palavras normaliza "001" para 1, e o prefixo
    /// casaria com qualquer número que começa por 1 (como "010" e "1793").
    /// </summary>
    public string CondicaoFullText() =>
        string.Join(" AND ", Palavras.Select(p => p.All(char.IsAsciiDigit)
            ? $"\"{p}\""
            : $"(FORMSOF(INFLECTIONAL, \"{p}\") OR \"{p}*\")"));

    /// <summary>Texto único para os mesmos filtros; entra no <c>mac</c> do cursor (research R-05).</summary>
    public string TextoCanonico() => $"{string.Join(' ', Palavras.Order(StringComparer.Ordinal))}|{Situacao}";

    /// <summary>Nomes dos filtros usados, sem os valores, para os logs.</summary>
    public string NomesUsados() => (Palavras.Count > 0, Situacao) switch
    {
        (true, SituacaoProduto.Todos) => "busca",
        (true, _) => "busca,situacao",
        (false, SituacaoProduto.Todos) => "nenhum",
        _ => "situacao"
    };

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Palavra();
}
