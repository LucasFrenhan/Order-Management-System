namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>A métrica de renovações por resultado, atrás de uma porta (REQ-24, R-10).</summary>
public interface IMetricasAutenticacao
{
    void RenovacaoConcluida(SituacaoRotacao situacao);
}
