using System.Text.Json.Serialization;

namespace PedidoFulfillment.Application.Contratos.Produtos;

/// <summary>
/// Um produto do catálogo (REQ-22, FR-005). Os campos da operação (Admin e OperadorLogistica)
/// são omitidos, e não nulos, na resposta ao Cliente, que vê só a disponibilidade (clarify).
/// </summary>
public sealed record ProdutoResponseDTO
{
    public required Guid Id { get; init; }

    public required string Sku { get; init; }

    public required string Nome { get; init; }

    public required string? Descricao { get; init; }

    public required decimal Preco { get; init; }

    public required string Moeda { get; init; }

    /// <summary>Há ao menos uma unidade em estoque.</summary>
    public required bool Disponivel { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? QuantidadeEstoque { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Ativo { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? CriadoEm { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? AtualizadoEm { get; init; }
}
