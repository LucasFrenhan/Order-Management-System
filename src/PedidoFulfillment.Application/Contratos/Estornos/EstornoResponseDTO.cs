using System.ComponentModel.DataAnnotations;

namespace PedidoFulfillment.Application.Contratos.Estornos;

/// <summary>
/// Um estorno para a operação (REQ-28, FR-002 e FR-004). Nunca traz os identificadores da transação
/// ou do estorno no gateway; o último erro é só a categoria (R-05).
/// </summary>
public sealed record EstornoResponseDTO
{
    /// <summary>Identificador do estorno (R-01).</summary>
    public required Guid EstornoId { get; init; }

    public required Guid PedidoId { get; init; }

    /// <summary>Número do pedido; o atributo descreve o formato no schema, como nas respostas de pedido.</summary>
    [RegularExpression(@"^PED-\d{8}-\d{6}$")]
    public required string Numero { get; init; }

    public required decimal Valor { get; init; }

    public required SituacaoEstorno Situacao { get; init; }

    /// <summary>Quando o cancelamento pediu o estorno.</summary>
    public required DateTimeOffset SolicitadoEm { get; init; }

    /// <summary>Tentativas com falha.</summary>
    public required int Tentativas { get; init; }

    public required DateTimeOffset? UltimaFalhaEm { get; init; }

    public required string? UltimoErro { get; init; }

    /// <summary>A mesma regra do log 7008; sempre <see langword="false"/> fora de <see cref="SituacaoEstorno.Pendente"/>.</summary>
    public required bool AlemDoLimiteAlerta { get; init; }

    /// <summary>Quando saiu o <c>pagamento.estorno-atrasado</c>; nulo se não saiu.</summary>
    public required DateTimeOffset? AtrasadoEm { get; init; }

    /// <summary>Tentativa forçada ainda não feita.</summary>
    public required DateTimeOffset? TentativaForcadaEm { get; init; }

    public required DateTimeOffset? ConfirmadoEm { get; init; }

    public required BaixaEstornoResponseDTO? Baixa { get; init; }
}
