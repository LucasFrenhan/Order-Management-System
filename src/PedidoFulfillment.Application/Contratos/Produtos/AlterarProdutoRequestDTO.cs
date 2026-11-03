using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Application.Contratos.Produtos;

/// <summary>Alteração de nome, descrição e preço (REQ-22, FR-012); o SKU e o estoque não mudam.</summary>
public sealed record AlterarProdutoRequestDTO
{
    [Required(ErrorMessage = MensagensCatalogo.Nome)]
    [MaxLength(Produto.TamanhoMaximoNome, ErrorMessage = MensagensCatalogo.Nome)]
    public string Nome { get; init => field = value?.Trim() ?? ""; } = "";

    [MaxLength(Produto.TamanhoMaximoDescricao, ErrorMessage = MensagensCatalogo.Descricao)]
    public string? Descricao { get; init => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }

    [PrecoCatalogo]
    public decimal Preco { get; init; }
}
