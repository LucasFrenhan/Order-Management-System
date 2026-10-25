namespace PedidoFulfillment.Application.Mensageria;

/// <summary>
/// Os consumidores do fluxo do pedido (REQ-14, R-03). Os manipuladores leem o nome daqui, e a
/// API valida o consumidor do reenvio da DLQ por esta lista, sem registrar os consumidores; um
/// teste confere que ela bate com os descritores do Worker.
/// </summary>
public static class ConsumidoresConhecidos
{
    public const string Estoque = "estoque";

    public const string Pagamento = "pagamento";

    public const string Entrega = "entrega";

    public static readonly IReadOnlyList<string> Nomes = [Estoque, Pagamento, Entrega];

    public static bool Conhecido(string? consumidor) => consumidor is not null && Nomes.Contains(consumidor, StringComparer.Ordinal);
}
