using PedidoFulfillment.Domain.Pagamentos;

namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>Um estorno lido para a operação (REQ-28), sem os identificadores do gateway.</summary>
public sealed record EstornoLido(
    Guid EstornoId,
    Guid PedidoId,
    string Numero,
    decimal Valor,
    SituacaoPagamento Situacao,
    DateTimeOffset SolicitadoEm,
    int Tentativas,
    DateTimeOffset? UltimaFalhaEm,
    string? UltimoErro,
    DateTimeOffset? AtrasadoEm,
    DateTimeOffset? TentativaForcadaEm,
    DateTimeOffset? ConfirmadoEm,
    DateTimeOffset? BaixadoEm,
    Guid? BaixadoPorId,
    string? MotivoBaixa,
    string? ReferenciaExternaBaixa);

/// <summary>Leitura dos estornos para a API (REQ-28, R-08), sem rastreamento.</summary>
public interface IConsultaEstornos
{
    /// <summary>
    /// Pendentes na ordem <c>EstornoSolicitadoEm, Id</c>, a partir da posição, no sentido dela, até a
    /// quantidade pedida; a lista sai sempre em ordem crescente.
    /// </summary>
    Task<IReadOnlyList<EstornoLido>> ListarPendentesAsync(PosicaoCursorEstornos? posicao, int quantidade, CancellationToken cancellationToken);

    /// <summary>Pendente, confirmado ou baixado; nulo para outro pagamento ou id desconhecido.</summary>
    Task<EstornoLido?> ObterAsync(Guid estornoId, CancellationToken cancellationToken);
}
