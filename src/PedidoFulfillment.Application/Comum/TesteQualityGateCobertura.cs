namespace PedidoFulfillment.Application.Comum;

/// <summary>PR descartável do REQ-20 (T017): código novo sem testes, para a cobertura reprovar. Não mergear.</summary>
public static class TesteQualityGateCobertura
{
    public static string Classificar(int quantidade)
    {
        if (quantidade < 0)
        {
            return "negativa";
        }

        if (quantidade == 0)
        {
            return "zero";
        }

        if (quantidade < 10)
        {
            return "pequena";
        }

        return quantidade < 100 ? "media" : "grande";
    }
}
