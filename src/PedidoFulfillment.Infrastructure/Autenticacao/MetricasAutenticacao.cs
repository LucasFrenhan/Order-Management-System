using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Infrastructure.Observabilidade;

namespace PedidoFulfillment.Infrastructure.Autenticacao;

/// <summary>Renovações por resultado (REQ-24, R-10), sem token, conta nem origem.</summary>
internal sealed class MetricasAutenticacao : IMetricasAutenticacao
{
    public void RenovacaoConcluida(SituacaoRotacao situacao)
    {
        var resultado = situacao switch
        {
            SituacaoRotacao.Renovada => TelemetriaOms.Resultados.Renovada,
            SituacaoRotacao.Reutilizada => TelemetriaOms.Resultados.Reutilizada,
            _ => TelemetriaOms.Resultados.Recusada
        };
        TelemetriaOms.Renovacoes.Add(1, new KeyValuePair<string, object?>(TelemetriaOms.Atributos.Resultado, resultado));
    }
}
