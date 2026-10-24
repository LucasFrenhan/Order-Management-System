using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Pagamentos;

/// <summary>
/// Gateway simulado (REQ-09, R-02): aprova cada cobrança com a chance configurada, sem chamada
/// externa. Os testes fixam a taxa em 100 ou 0, ou passam um <see cref="Random"/> com semente.
/// </summary>
internal sealed class GatewayPagamentoSimulado : IGatewayPagamento
{
    public const string MotivoRecusa = "Pagamento recusado pelo emissor.";

    private readonly double taxaAprovacao;
    private readonly Random aleatorio;

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
        Task.FromResult(aleatorio.NextDouble() * 100 < taxaAprovacao
            ? ResultadoCobranca.Aprovada($"SIM-{Guid.CreateVersion7():N}")
            : ResultadoCobranca.Recusada(MotivoRecusa));

    /// <summary>O estorno simulado sempre confirma (REQ-13, R-03); os testes forçam falhas com gateways falsos.</summary>
    public Task<ResultadoEstorno> EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken cancellationToken) =>
        Task.FromResult(ResultadoEstorno.Confirmar($"SIM-EST-{Guid.CreateVersion7():N}"));
}
