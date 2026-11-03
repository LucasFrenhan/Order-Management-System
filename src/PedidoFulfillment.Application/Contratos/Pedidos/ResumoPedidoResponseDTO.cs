using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Contratos.Pedidos;

/// <summary>
/// Um pedido na listagem (REQ-21, FR-018). Os campos têm os mesmos nomes, formatos e valores do
/// <see cref="PedidoResponseDTO"/> (FR-020); itens, histórico e entrega ficam na consulta por id.
/// </summary>
public sealed record ResumoPedidoResponseDTO
{
    public required Guid Id { get; init; }

    [RegularExpression(@"^PED-\d{8}-\d{6}$")]
    public required string Numero { get; init; }

    public required Guid ClienteId { get; init; }

    public required StatusPedido Status { get; init; }

    public required decimal ValorTotal { get; init; }

    // Sempre "BRL", como no PedidoResponseDTO.
    [Required]
    public string Moeda { get; init; } = "BRL";

    /// <summary>Linhas de item (produtos distintos), não a soma das quantidades (research R-08).</summary>
    public required int QuantidadeItens { get; init; }

    public required DateTimeOffset CriadoEm { get; init; }

    public required DateTimeOffset AtualizadoEm { get; init; }
}
