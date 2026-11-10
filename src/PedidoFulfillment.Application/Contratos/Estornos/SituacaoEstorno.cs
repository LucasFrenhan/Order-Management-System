namespace PedidoFulfillment.Application.Contratos.Estornos;

/// <summary>
/// Situação do estorno no contrato (REQ-28). Enum próprio: a <c>SituacaoPagamento</c> continua
/// interna (REQ-13, clarify), e acrescentar um valor aqui muda o contrato.
/// </summary>
public enum SituacaoEstorno
{
    /// <summary>Esperando o gateway; a rodada tenta de novo.</summary>
    Pendente,

    /// <summary>O gateway confirmou o estorno.</summary>
    Confirmado,

    /// <summary>Um Admin deu baixa: o estorno foi resolvido fora do sistema.</summary>
    Baixado
}
