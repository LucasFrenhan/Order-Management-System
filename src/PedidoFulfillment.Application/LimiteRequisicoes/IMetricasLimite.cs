namespace PedidoFulfillment.Application.LimiteRequisicoes;

/// <summary>Métricas do limite (FR-021), sem dados pessoais.</summary>
public interface IMetricasLimite
{
    void RequisicaoRecusada(string politica);

    void RequisicaoSemLimite();
}
