using PedidoFulfillment.Application.LimiteRequisicoes;
using PedidoFulfillment.Infrastructure.Observabilidade;

namespace PedidoFulfillment.Infrastructure.LimiteRequisicoes;

/// <summary>Liga as métricas do limite à <see cref="TelemetriaOms"/>, que é interna à Infrastructure.</summary>
internal sealed class MetricasLimite : IMetricasLimite
{
    public void RequisicaoRecusada(string politica) =>
        TelemetriaOms.RequisicoesRecusadas.Add(1, new KeyValuePair<string, object?>(TelemetriaOms.Atributos.LimitePolitica, politica));

    public void RequisicaoSemLimite() => TelemetriaOms.FalhasArmazenamentoLimite.Add(1);
}
