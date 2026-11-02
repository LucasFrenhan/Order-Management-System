namespace PedidoFulfillment.Application.Comum;

/// <summary>PR descartável do REQ-20 (T017): bug proposital para o Quality Gate reprovar. Não mergear.</summary>
public static class TesteQualityGateBug
{
    public static int Contar(IEnumerable<int> itens)
    {
        var vistos = new List<int>();
        if (vistos.Count > 0)
        {
            return vistos.Count;
        }

        return itens.Count();
    }
}
