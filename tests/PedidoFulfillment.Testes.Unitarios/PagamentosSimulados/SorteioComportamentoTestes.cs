using FluentAssertions;
using PedidoFulfillment.PagamentosSimulados.Operacoes;
using PedidoFulfillment.PagamentosSimulados.Simulacao;

namespace PedidoFulfillment.Testes.Unitarios.PagamentosSimulados;

/// <summary>Sorteio com as chances padrão (REQ-19, FR-003 e FR-004, research R-03), com semente fixa.</summary>
public sealed class SorteioComportamentoTestes
{
    private const int Sorteios = 20_000;

    [Fact]
    public void As_proporcoes_ficam_perto_das_chances_padrao()
    {
        var parametros = new OpcoesSimulacao();
        var aleatorio = new Random(20261102);

        var contagem = Enumerable.Range(0, Sorteios)
            .Select(_ => DecisorComportamento.Sortear(parametros, TipoOperacao.Cobranca, aleatorio))
            .GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count() / (double)Sorteios);

        contagem[Comportamento.Indisponivel].Should().BeApproximately(0.02, 0.005);
        contagem[Comportamento.LimiteTaxa].Should().BeApproximately(0.01, 0.004);
        contagem[Comportamento.ErroInterno].Should().BeApproximately(0.01, 0.004);
        contagem[Comportamento.SemResposta].Should().BeApproximately(0.01, 0.004);
        contagem[Comportamento.ProcessarSemResponder].Should().BeApproximately(0.01, 0.004);
        var aprovadas = contagem[Comportamento.Aprovar];
        var recusadas = contagem[Comportamento.Recusar];
        (aprovadas / (aprovadas + recusadas)).Should().BeApproximately(0.90, 0.01, "a aprovação vale sobre as chamadas que efetivam");
    }

    [Fact]
    public void A_latencia_varia_com_a_maior_parte_rapida_e_uma_cauda_lenta()
    {
        var parametros = new OpcoesSimulacao();
        var aleatorio = new Random(20261102);

        var latencias = Enumerable.Range(0, Sorteios).Select(_ => DecisorComportamento.SortearLatencia(parametros, aleatorio)).ToList();

        latencias.Should().OnlyContain(l => (l >= 40 && l <= 250) || (l >= 1000 && l <= 5000));
        (latencias.Count(l => l >= 1000) / (double)Sorteios).Should().BeApproximately(0.05, 0.01);
        latencias.Where(l => l <= 250).Distinct().Count().Should().BeGreaterThan(100, "o tempo não é fixo (FR-003)");
    }

    [Fact]
    public void Sem_cauda_a_latencia_fica_na_faixa_rapida()
    {
        var parametros = new OpcoesSimulacao { CaudaPercentual = 0, LatenciaMinimaMs = 0, LatenciaMaximaMs = 0 };

        DecisorComportamento.SortearLatencia(parametros, new Random(1)).Should().Be(0);
    }
}
