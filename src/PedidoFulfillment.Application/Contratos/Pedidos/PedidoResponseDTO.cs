using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Contratos.Pedidos;

/// <summary>
/// Pedido devolvido pela API (FR-019). Datas em UTC; valores com duas casas decimais.
/// </summary>
public sealed record PedidoResponseDTO
{
    public required Guid Id { get; init; }

    [RegularExpression(@"^PED-\d{8}-\d{6}$")]
    public required string Numero { get; init; }

    public required Guid ClienteId { get; init; }

    public required StatusPedido Status { get; init; }

    public required IReadOnlyList<ItemPedidoResponseDTO> Itens { get; init; }

    public required decimal ValorTotal { get; init; }

    // Sempre "BRL"; [Required] mantém o campo obrigatório no schema sem exigir que o serviço o informe.
    [Required]
    public string Moeda { get; init; } = "BRL";

    public required DateTimeOffset CriadoEm { get; init; }

    public required DateTimeOffset AtualizadoEm { get; init; }
}
