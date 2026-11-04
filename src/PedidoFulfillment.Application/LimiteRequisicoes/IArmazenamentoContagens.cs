namespace PedidoFulfillment.Application.LimiteRequisicoes;

/// <summary>Uma contagem a verificar: a identidade em forma não legível, o limite e a janela.</summary>
public sealed record ContagemSolicitada(byte[] Chave, int Limite, int JanelaSegundos);

/// <summary>
/// Uma contagem que recusou a requisição. <see cref="Indice"/> é a posição dela na lista pedida, e
/// <see cref="PrimeiroBloqueio"/> indica a primeira recusa dessa contagem na janela (FR-020).
/// </summary>
public sealed record RecusaContagem(int Indice, TimeSpan Espera, bool PrimeiroBloqueio);

/// <summary>
/// Estado único dos limites para todas as instâncias da API (FR-006). A operação é atômica: ou todas
/// as contagens aceitam e são incrementadas, ou nenhuma muda (FR-002, FR-007).
/// </summary>
public interface IArmazenamentoContagens
{
    /// <returns>As recusas; uma lista vazia significa que a requisição foi aceita e contada.</returns>
    Task<IReadOnlyList<RecusaContagem>> ContarAsync(IReadOnlyList<ContagemSolicitada> contagens, CancellationToken cancellationToken);
}

/// <summary>
/// A operação perdeu uma disputa momentânea no armazenamento (deadlock). Vale uma nova tentativa, e
/// não indica que o armazenamento está fora do ar (R-03).
/// </summary>
public sealed class DisputaContagemException(string mensagem, Exception causa) : Exception(mensagem, causa);
