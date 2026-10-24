using System.Collections.Concurrent;

namespace PedidoFulfillment.Application.Reconciliacao;

/// <summary>
/// Em quantos ciclos a regra falhou para cada pedido, desde o último efeito gravado (REQ-12,
/// R-06). Fica só na memória do Worker, sem gravar nada no pedido (clarify): reiniciar zera a
/// contagem, o que só adia o esgotamento.
/// </summary>
public sealed class RegistroFalhasReconciliacao
{
    private readonly ConcurrentDictionary<Guid, (int Falhas, DateTimeOffset UltimaFalhaEm)> entradas = new();

    /// <summary>Os pedidos que já falharam; vão para o fim do lote (FR-011).</summary>
    public IReadOnlyCollection<Guid> ComFalha => [.. entradas.Keys];

    /// <summary>Conta mais um ciclo com falha e devolve o total.</summary>
    public int RegistrarFalha(Guid pedidoId, DateTimeOffset agora) =>
        entradas.AddOrUpdate(pedidoId, _ => (1, agora), (_, atual) => (atual.Falhas + 1, agora)).Falhas;

    /// <summary>Efeito gravado, pedido não mais parado ou esgotado.</summary>
    public void Remover(Guid pedidoId) => entradas.TryRemove(pedidoId, out _);

    /// <summary>
    /// Apaga as entradas sem falha desde <paramref name="antesDe"/>: o pedido deixou de aparecer
    /// como candidato, por exemplo porque a operação o moveu.
    /// </summary>
    public void Podar(DateTimeOffset antesDe)
    {
        foreach (var entrada in entradas)
        {
            if (entrada.Value.UltimaFalhaEm < antesDe)
            {
                entradas.TryRemove(entrada);
            }
        }
    }
}
