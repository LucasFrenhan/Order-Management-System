using System.Collections.Concurrent;
using PedidoFulfillment.Infrastructure.Observabilidade;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Inscrito ou sem inscrição, por consumidor conhecido (REQ-17, data-model): o
/// <see cref="SupervisorInscricao"/> grava, a prontidão (<c>VerificacaoConsumidores</c>) lê. Por
/// host, para que dois Workers no mesmo processo de teste não se misturem; o gauge da métrica 7
/// lê o espelho estático em <see cref="EstadoMetricas"/>.
/// </summary>
internal sealed class EstadoConsumidores
{
    private readonly Lazy<ConcurrentDictionary<string, Inscricao>> inscricoes;

    public EstadoConsumidores(IEnumerable<IDescritorConsumidor> descritores)
    {
        // Lido no primeiro uso: o descritor resolve o manipulador num escopo, como na TopologiaConhecida.
        inscricoes = new(() =>
        {
            var todas = new ConcurrentDictionary<string, Inscricao>(StringComparer.Ordinal);
            foreach (var descritor in descritores)
            {
                todas[descritor.Consumidor] = new Inscricao(false, null);
                EstadoMetricas.RegistrarInscricao(descritor.Consumidor, inscrito: false);
            }

            return todas;
        });
    }

    /// <summary>Pronto só com pelo menos um consumidor e todos inscritos (FR-013).</summary>
    public bool TodosInscritos => !inscricoes.Value.IsEmpty && inscricoes.Value.Values.All(i => i.Inscrito);

    public void MarcarInscrito(string consumidor)
    {
        inscricoes.Value[consumidor] = new Inscricao(true, null);
        EstadoMetricas.RegistrarInscricao(consumidor, inscrito: true);
    }

    /// <summary>Guarda o instante só na primeira perda: as tentativas seguintes não o adiam.</summary>
    public void MarcarSemInscricao(string consumidor, DateTimeOffset instante)
    {
        inscricoes.Value.AddOrUpdate(
            consumidor,
            _ => new Inscricao(false, instante),
            (_, atual) => atual.Inscrito || atual.SemInscricaoDesde is null ? new Inscricao(false, instante) : atual);
        EstadoMetricas.RegistrarInscricao(consumidor, inscrito: false);
    }

    public DateTimeOffset? SemInscricaoDesde(string consumidor) =>
        inscricoes.Value.TryGetValue(consumidor, out var inscricao) ? inscricao.SemInscricaoDesde : null;

    private sealed record Inscricao(bool Inscrito, DateTimeOffset? SemInscricaoDesde);
}
