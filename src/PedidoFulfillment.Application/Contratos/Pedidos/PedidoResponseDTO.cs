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

    /// <summary>
    /// Mudanças de status em ordem cronológica (REQ-11, FR-002). Opcional no schema, como pede a
    /// v1 para campos novos, e sempre preenchida pela API; uma resposta idempotente gravada antes do
    /// REQ-11 volta com a lista vazia (research R-03).
    /// </summary>
    public IReadOnlyList<HistoricoStatusPedidoResponseDTO> Historico { get; init; } = [];

    /// <summary>Ordem de entrega; nula enquanto não existe (REQ-11, FR-004).</summary>
    public EntregaPedidoResponseDTO? Entrega { get; init; }
}
