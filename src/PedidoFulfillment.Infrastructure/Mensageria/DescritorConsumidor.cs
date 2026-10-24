using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Mensageria;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Um consumidor conhecido do Worker: nome e tipos de evento (REQ-12, R-01). Nasce no mesmo
/// registro do consumidor, então um consumidor novo entra na topologia declarada pelo
/// despachante sem passo manual (FR-003).
/// </summary>
internal interface IDescritorConsumidor
{
    string Consumidor { get; }

    IReadOnlyCollection<string> Tipos { get; }
}

/// <summary>Lê nome e tipos do manipulador num escopo, como <see cref="ConsumidorEventos{TManipulador, TDados}"/> na partida.</summary>
internal sealed class DescritorConsumidor<TManipulador, TDados>(IServiceScopeFactory escopos) : IDescritorConsumidor
    where TManipulador : class, IManipuladorEvento<TDados>
{
    private readonly Lazy<(string Consumidor, IReadOnlyCollection<string> Tipos)> lido = new(() =>
    {
        using var escopo = escopos.CreateScope();
        var manipulador = escopo.ServiceProvider.GetRequiredService<TManipulador>();
        return (manipulador.Consumidor, [.. manipulador.Tipos]);
    });

    public string Consumidor => lido.Value.Consumidor;

    public IReadOnlyCollection<string> Tipos => lido.Value.Tipos;
}
