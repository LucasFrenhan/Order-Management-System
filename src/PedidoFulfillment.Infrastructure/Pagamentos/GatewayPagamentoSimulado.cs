using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Pagamentos;

/// <summary>
/// Gateway simulado (REQ-09, R-02): aprova cada cobrança com a chance configurada, sem chamada
/// externa. Os testes fixam a taxa em 100 ou 0, ou passam um <see cref="Random"/> com semente.
/// </summary>
/// <remarks>
/// O pedido é a chave de idempotência da cobrança e do estorno (REQ-13, R-03): a mesma chave
/// devolve o mesmo resultado, sem novo sorteio. As chaves ficam na memória deste singleton e se
/// perdem quando o Worker reinicia; isso só é aceitável porque a simulação não tem efeito
/// externo. Um gateway real guarda as chaves do lado dele.
/// </remarks>
internal sealed class GatewayPagamentoSimulado : IGatewayPagamento
{
    public const string MotivoRecusa = "Pagamento recusado pelo emissor.";

    private readonly double taxaAprovacao;
    private readonly Random aleatorio;
    private readonly ConcurrentDictionary<Guid, Lazy<ResultadoCobranca>> cobrancas = new();
    private readonly ConcurrentDictionary<Guid, Lazy<ResultadoEstorno>> estornos = new();

    public GatewayPagamentoSimulado(IOptions<OpcoesGatewayPagamento> opcoes)
        : this(opcoes, Random.Shared)
    {
    }

    internal GatewayPagamentoSimulado(IOptions<OpcoesGatewayPagamento> opcoes, Random aleatorio)
    {
        taxaAprovacao = opcoes.Value.TaxaAprovacaoPercentual;
        this.aleatorio = aleatorio;
    }

    public Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken) =>
        Task.FromResult(cobrancas.GetOrAdd(pedidoId, _ => new Lazy<ResultadoCobranca>(Sortear)).Value);

    /// <summary>O estorno simulado sempre confirma (REQ-13, R-03); os testes forçam falhas com gateways falsos.</summary>
    public Task<ResultadoEstorno> EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken cancellationToken) =>
        Task.FromResult(estornos.GetOrAdd(pedidoId, _ => new Lazy<ResultadoEstorno>(
            () => ResultadoEstorno.Confirmar($"SIM-EST-{Guid.CreateVersion7():N}"))).Value);

    private ResultadoCobranca Sortear() =>
        aleatorio.NextDouble() * 100 < taxaAprovacao
            ? ResultadoCobranca.Aprovada($"SIM-{Guid.CreateVersion7():N}")
            : ResultadoCobranca.Recusada(MotivoRecusa);
}
