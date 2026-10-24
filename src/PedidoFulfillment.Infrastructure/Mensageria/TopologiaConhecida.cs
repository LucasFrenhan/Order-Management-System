namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Os consumidores conhecidos e os tipos de evento que exigem rota (REQ-12, R-01, R-03). Um
/// tipo com consumidor sai com <c>mandatory</c>; os demais, como <c>entrega.criada</c>, saem
/// como antes (FR-004, FR-005).
/// </summary>
internal sealed class TopologiaConhecida
{
    private readonly Lazy<(IReadOnlyList<(string Consumidor, IReadOnlyCollection<string> Tipos)> Consumidores, HashSet<string> ComRota)> montada;

    public TopologiaConhecida(IEnumerable<IDescritorConsumidor> descritores)
    {
        // Lida na primeira publicação: o descritor resolve o manipulador num escopo.
        montada = new(() =>
        {
            var consumidores = descritores.Select(d => (d.Consumidor, d.Tipos)).ToList();
            return (consumidores, consumidores.SelectMany(c => c.Tipos).ToHashSet(StringComparer.Ordinal));
        });
    }

    public IReadOnlyList<(string Consumidor, IReadOnlyCollection<string> Tipos)> Consumidores => montada.Value.Consumidores;

    public bool ExigeRota(string tipo) => montada.Value.ComRota.Contains(tipo);
}
