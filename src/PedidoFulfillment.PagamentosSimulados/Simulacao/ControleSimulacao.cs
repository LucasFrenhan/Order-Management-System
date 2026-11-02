using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using PedidoFulfillment.PagamentosSimulados.Operacoes;

namespace PedidoFulfillment.PagamentosSimulados.Simulacao;

/// <summary>
/// Estado do comportamento simulado (REQ-19, FR-007a, research R-03), em memória: as chances e a
/// latência em vigor, o modo forçado e o roteiro. A decisão segue a ordem roteiro → modo forçado →
/// sorteio. Volta à configuração no reinício e em <see cref="Restaurar"/>.
/// </summary>
public sealed class ControleSimulacao
{
    private readonly OpcoesSimulacao configuracao;
    private readonly Random aleatorio;
    private readonly ConcurrentQueue<Comportamento> roteiro = new();
    private readonly Lock trava = new();
    private OpcoesSimulacao emVigor;
    private Comportamento? modoForcado;
    private long chamadasRecebidas;

    public ControleSimulacao(IOptions<OpcoesSimulacao> opcoes)
    {
        configuracao = opcoes.Value.Copiar();
        aleatorio = configuracao.Semente is { } semente ? new Random(semente) : new Random();
        emVigor = Inicial(configuracao);
    }

    public Comportamento? ModoForcado
    {
        get
        {
            lock (trava)
            {
                return modoForcado;
            }
        }
    }

    public int TamanhoRoteiro => roteiro.Count;

    /// <summary>Chamadas de cobrança, estorno e consulta decididas desde a partida ou o último <see cref="Restaurar"/>.</summary>
    public long ChamadasRecebidas => Interlocked.Read(ref chamadasRecebidas);

    /// <summary>Uma cópia das chances e da latência em vigor.</summary>
    public OpcoesSimulacao Parametros
    {
        get
        {
            lock (trava)
            {
                return emVigor.Copiar();
            }
        }
    }

    /// <summary>
    /// Roteiro, depois modo forçado, depois sorteio. A latência vale para todas as chamadas,
    /// inclusive as consultas.
    /// </summary>
    public DecisaoComportamento Decidir(TipoOperacao tipo)
    {
        Interlocked.Increment(ref chamadasRecebidas);
        lock (trava)
        {
            var latencia = DecisorComportamento.SortearLatencia(emVigor, aleatorio);
            if (roteiro.TryDequeue(out var proximo))
            {
                return new DecisaoComportamento(proximo, latencia, Sorteado: false);
            }

            return modoForcado is { } modo
                ? new DecisaoComportamento(modo, latencia, Sorteado: false)
                : new DecisaoComportamento(DecisorComportamento.Sortear(emVigor, tipo, aleatorio), latencia, Sorteado: true);
        }
    }

    /// <summary>Troca as chances e a latência; devolve as violações, sem mudar nada se houver alguma.</summary>
    public IReadOnlyList<string> Alterar(Action<OpcoesSimulacao> alteracao)
    {
        lock (trava)
        {
            var novo = emVigor.Copiar();
            alteracao(novo);
            var erros = novo.Validar();
            if (erros.Count == 0)
            {
                emVigor = novo;
            }

            return erros;
        }
    }

    public void ForcarModo(Comportamento comportamento)
    {
        lock (trava)
        {
            modoForcado = comportamento;
        }
    }

    public void DesfazerModo()
    {
        lock (trava)
        {
            modoForcado = null;
        }
    }

    public void Enfileirar(IEnumerable<Comportamento> comportamentos)
    {
        lock (trava)
        {
            foreach (var comportamento in comportamentos)
            {
                roteiro.Enqueue(comportamento);
            }
        }
    }

    public void EsvaziarRoteiro() => roteiro.Clear();

    /// <summary>Volta às chances da configuração, sem modo forçado e sem roteiro.</summary>
    public void Restaurar()
    {
        lock (trava)
        {
            emVigor = Inicial(configuracao);
            modoForcado = null;
            roteiro.Clear();
            Interlocked.Exchange(ref chamadasRecebidas, 0);
        }
    }

    /// <summary>No modo determinístico (FR-007), a partida zera as falhas e a latência e só aprova.</summary>
    private static OpcoesSimulacao Inicial(OpcoesSimulacao configuracao)
    {
        var inicial = configuracao.Copiar();
        if (inicial.Deterministico)
        {
            inicial.LatenciaMinimaMs = 0;
            inicial.LatenciaMaximaMs = 0;
            inicial.CaudaPercentual = 0;
            inicial.TaxaAprovacaoPercentual = 100;
            inicial.IndisponivelPercentual = 0;
            inicial.LimiteTaxaPercentual = 0;
            inicial.ErroInternoPercentual = 0;
            inicial.SemRespostaPercentual = 0;
            inicial.ProcessarSemResponderPercentual = 0;
        }

        return inicial;
    }
}
