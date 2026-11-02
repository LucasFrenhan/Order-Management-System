using System.Collections.Concurrent;
using PedidoFulfillment.Application.Pagamentos;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Gateway em memória, só para os testes que não exercitam o pagamento pela rede (REQ-19, FR-009a
/// e research R-11; antes o <c>GatewayPagamentoSimulado</c> do Worker, REQ-09 R-02): aprova cada
/// cobrança com a chance dada, sem chamada externa. A <see cref="FabricaWorker"/> o registra por
/// padrão, com a taxa de <c>GatewayPagamentoMemoria:TaxaAprovacaoPercentual</c> (100 se ausente).
/// </summary>
/// <remarks>
/// O pedido é a chave de idempotência da cobrança e do estorno (REQ-13, R-03): a mesma chave
/// devolve o mesmo resultado, sem novo sorteio, e a consulta devolve a cobrança guardada.
/// </remarks>
public sealed class GatewayPagamentoMemoria(double taxaAprovacao = 100, Random? aleatorio = null) : IGatewayPagamento
{
    public const string ChaveTaxa = "GatewayPagamentoMemoria:TaxaAprovacaoPercentual";
    public const string MotivoRecusa = "Pagamento recusado pelo emissor.";

    private readonly Random aleatorio = aleatorio ?? Random.Shared;
    private readonly ConcurrentDictionary<Guid, Lazy<ResultadoCobranca>> cobrancas = new();
    private readonly ConcurrentDictionary<Guid, Lazy<ResultadoEstorno>> estornos = new();

    public Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken) =>
        Task.FromResult(cobrancas.GetOrAdd(pedidoId, _ => new Lazy<ResultadoCobranca>(Sortear)).Value);

    /// <summary>O estorno em memória sempre confirma (REQ-13, R-03); os testes forçam falhas com gateways falsos.</summary>
    public Task<ResultadoEstorno> EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken cancellationToken) =>
        Task.FromResult(estornos.GetOrAdd(pedidoId, _ => new Lazy<ResultadoEstorno>(
            () => ResultadoEstorno.Confirmar($"SIM-EST-{Guid.CreateVersion7():N}"))).Value);

    public Task<ConsultaCobranca> ConsultarCobrancaAsync(Guid pedidoId, CancellationToken cancellationToken) =>
        Task.FromResult(cobrancas.TryGetValue(pedidoId, out var cobranca)
            ? cobranca.Value.Aprovado ? ConsultaCobranca.Aprovada(cobranca.Value.IdentificadorTransacao!) : ConsultaCobranca.Recusada(cobranca.Value.Motivo!)
            : ConsultaCobranca.NaoEncontrada);

    private ResultadoCobranca Sortear() =>
        aleatorio.NextDouble() * 100 < taxaAprovacao
            ? ResultadoCobranca.Aprovada($"SIM-{Guid.CreateVersion7():N}")
            : ResultadoCobranca.Recusada(MotivoRecusa);
}
