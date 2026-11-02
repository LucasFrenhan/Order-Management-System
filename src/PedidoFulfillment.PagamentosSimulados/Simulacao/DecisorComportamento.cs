using PedidoFulfillment.PagamentosSimulados.Operacoes;

namespace PedidoFulfillment.PagamentosSimulados.Simulacao;

/// <summary>
/// O comportamento escolhido para uma chamada, a latência a aplicar antes de responder e se veio do
/// sorteio (a consulta ignora as falhas sorteadas, mas não as do roteiro ou do modo forçado).
/// </summary>
public sealed record DecisaoComportamento(Comportamento Comportamento, int LatenciaMs, bool Sorteado);

/// <summary>
/// Sorteio pelas chances em vigor (REQ-19, research R-03, passo 3): primeiro as falhas, pela soma
/// acumulada; depois, entre as chamadas que efetivam, aprovação ou recusa. O estorno sorteado não
/// é recusado: a recusa dele vem só das regras do estorno (contrato › Estorno). A latência é
/// sorteada à parte, com a cauda lenta.
/// </summary>
public static class DecisorComportamento
{
    public static Comportamento Sortear(OpcoesSimulacao parametros, TipoOperacao tipo, Random aleatorio)
    {
        var sorteio = aleatorio.NextDouble() * 100;
        var acumulado = 0d;
        foreach (var (comportamento, chance) in Falhas(parametros))
        {
            acumulado += chance;
            if (sorteio < acumulado)
            {
                return comportamento;
            }
        }

        if (tipo == TipoOperacao.Estorno)
        {
            return Comportamento.Aprovar;
        }

        return aleatorio.NextDouble() * 100 < parametros.TaxaAprovacaoPercentual ? Comportamento.Aprovar : Comportamento.Recusar;
    }

    public static int SortearLatencia(OpcoesSimulacao parametros, Random aleatorio)
    {
        var cauda = parametros.CaudaPercentual > 0 && aleatorio.NextDouble() * 100 < parametros.CaudaPercentual;
        return cauda
            ? aleatorio.Next(parametros.CaudaMinimoMs, parametros.CaudaMaximoMs + 1)
            : aleatorio.Next(parametros.LatenciaMinimaMs, parametros.LatenciaMaximaMs + 1);
    }

    private static IEnumerable<(Comportamento, double)> Falhas(OpcoesSimulacao parametros)
    {
        yield return (Comportamento.Indisponivel, parametros.IndisponivelPercentual);
        yield return (Comportamento.LimiteTaxa, parametros.LimiteTaxaPercentual);
        yield return (Comportamento.ErroInterno, parametros.ErroInternoPercentual);
        yield return (Comportamento.SemResposta, parametros.SemRespostaPercentual);
        yield return (Comportamento.ProcessarSemResponder, parametros.ProcessarSemResponderPercentual);
    }
}
