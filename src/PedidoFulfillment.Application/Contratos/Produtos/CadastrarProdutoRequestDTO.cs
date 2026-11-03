using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Application.Contratos.Produtos;

/// <summary>Cadastro de produto pelo Admin (REQ-22, FR-009). Textos aparados no <c>init</c>.</summary>
public sealed record CadastrarProdutoRequestDTO
{
    [Required(ErrorMessage = MensagensCatalogo.Sku)]
    [MaxLength(Produto.TamanhoMaximoSku, ErrorMessage = MensagensCatalogo.Sku)]
    public string Sku { get; init => field = value?.Trim() ?? ""; } = "";

    [Required(ErrorMessage = MensagensCatalogo.Nome)]
    [MaxLength(Produto.TamanhoMaximoNome, ErrorMessage = MensagensCatalogo.Nome)]
    public string Nome { get; init => field = value?.Trim() ?? ""; } = "";

    [MaxLength(Produto.TamanhoMaximoDescricao, ErrorMessage = MensagensCatalogo.Descricao)]
    public string? Descricao { get; init => field = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }

    [PrecoCatalogo]
    public decimal Preco { get; init; }

    [Range(0, MensagensCatalogo.EstoqueMaximo, ErrorMessage = MensagensCatalogo.EstoqueInicial)]
    public int EstoqueInicial { get; init; }
}
