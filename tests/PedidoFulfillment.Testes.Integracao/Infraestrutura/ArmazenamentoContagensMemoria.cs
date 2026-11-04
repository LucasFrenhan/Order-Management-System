using PedidoFulfillment.Application.LimiteRequisicoes;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// O estado dos limites em memória, para a suíte que roda sem banco (REQ-23, R-10), com as mesmas
/// regras do armazenamento real: janela fixa, tudo ou nada e um só log de bloqueio por janela.
/// </summary>
public sealed class ArmazenamentoContagensMemoria : IArmazenamentoContagens
{
    private readonly Lock trava = new();
    private readonly Dictionary<string, Registro> registros = [];

    public Task<IReadOnlyList<RecusaContagem>> ContarAsync(IReadOnlyList<ContagemSolicitada> contagens, CancellationToken cancellationToken)
    {
        lock (trava)
        {
            var agora = DateTimeOffset.UtcNow;
            var chaves = contagens.Select(c => Convert.ToHexString(c.Chave)).ToArray();
            var atuais = chaves.Select(c => registros.TryGetValue(c, out var r) && r.Fim > agora ? r : new Registro()).ToArray();

            List<RecusaContagem> recusas = [];
            for (var i = 0; i < contagens.Count; i++)
            {
                if (atuais[i].Contador >= contagens[i].Limite)
                {
                    recusas.Add(new RecusaContagem(i, atuais[i].Fim - agora, !atuais[i].BloqueioRegistrado));
                    atuais[i].BloqueioRegistrado = true;
                }
            }

            if (recusas.Count > 0)
            {
                return Task.FromResult<IReadOnlyList<RecusaContagem>>(recusas);
            }

            for (var i = 0; i < contagens.Count; i++)
            {
                if (atuais[i].Contador == 0)
                {
                    atuais[i].Fim = agora.AddSeconds(contagens[i].JanelaSegundos);
                }

                atuais[i].Contador++;
                registros[chaves[i]] = atuais[i];
            }

            return Task.FromResult<IReadOnlyList<RecusaContagem>>([]);
        }
    }

    private sealed class Registro
    {
        public int Contador { get; set; }

        public DateTimeOffset Fim { get; set; }

        public bool BloqueioRegistrado { get; set; }
    }
}
