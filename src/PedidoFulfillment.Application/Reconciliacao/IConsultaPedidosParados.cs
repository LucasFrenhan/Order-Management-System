namespace PedidoFulfillment.Application.Reconciliacao;

/// <summary>
/// Seleção dos pedidos parados numa etapa do fluxo automático (REQ-12, R-05). Só escolhe
/// candidatos: a regra da etapa relê o pedido e decide pelo domínio.
/// </summary>
public interface IConsultaPedidosParados
{
    /// <summary>
    /// Até <paramref name="quantidade"/> pedidos com a última atualização anterior a
    /// <paramref name="paradoAntesDe"/>: primeiro os de fora de <paramref name="comFalha"/>, depois
    /// os de dentro, cada grupo do mais antigo para o mais novo (FR-011).
    /// </summary>
    Task<IReadOnlyList<PedidoParado>> ListarAsync(
        DateTimeOffset paradoAntesDe, int quantidade, IReadOnlyCollection<Guid> comFalha, CancellationToken cancellationToken);
}
