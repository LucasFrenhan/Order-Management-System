using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Contratos.Pedidos;

/// <summary>
/// Novo status de um pedido (FR-020). As transições permitidas por perfil são validadas pelo
/// serviço de aplicação; aqui só o formato da requisição.
/// </summary>
public sealed record AtualizarStatusPedidoRequestDTO : IValidatableObject
{
    // Não anulável para o schema bater com o contrato (I3). Ausente no JSON = 0, que não é um
    // valor de StatusPedido e é rejeitado em Validate.
    [Required]
    public StatusPedido Status { get; init; }

    [MaxLength(500, ErrorMessage = "O motivo deve ter até 500 caracteres.")]
    public string? Motivo { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(Status))
        {
            yield return new ValidationResult("O status é obrigatório.", [nameof(Status)]);
        }
        else if (Status == StatusPedido.PagamentoRejeitado)
        {
            yield return new ValidationResult(
                "O status PagamentoRejeitado não pode ser definido por esta operação.", [nameof(Status)]);
        }

        if (Status == StatusPedido.Cancelado && string.IsNullOrWhiteSpace(Motivo))
        {
            yield return new ValidationResult("O motivo é obrigatório quando o status é Cancelado.", [nameof(Motivo)]);
        }
    }
}
