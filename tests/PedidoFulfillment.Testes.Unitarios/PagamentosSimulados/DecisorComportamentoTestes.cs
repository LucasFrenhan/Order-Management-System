using FluentAssertions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.PagamentosSimulados.Operacoes;
using PedidoFulfillment.PagamentosSimulados.Simulacao;

namespace PedidoFulfillment.Testes.Unitarios.PagamentosSimulados;

/// <summary>Decisão do comportamento: roteiro → modo forçado → sorteio (REQ-19, research R-03).</summary>
public sealed class DecisorComportamentoTestes
{
    private static ControleSimulacao Controle(Action<OpcoesSimulacao>? ajuste = null)
    {
        var opcoes = new OpcoesSimulacao { Deterministico = true, Semente = 7 };
        ajuste?.Invoke(opcoes);
        return new ControleSimulacao(Options.Create(opcoes));
    }

    [Fact]
    public void No_modo_deterministico_so_aprova_e_sem_latencia()
    {
        var controle = Controle();

        Enumerable.Range(0, 200).Select(_ => controle.Decidir(TipoOperacao.Cobranca))
            .Should().OnlyContain(d => d.Comportamento == Comportamento.Aprovar && d.LatenciaMs == 0 && d.Sorteado);
    }

    [Fact]
    public void O_roteiro_vale_antes_do_modo_forcado_e_consome_um_item_por_chamada()
    {
        var controle = Controle();
        controle.ForcarModo(Comportamento.Indisponivel);
        controle.Enfileirar([Comportamento.ErroInterno, Comportamento.Recusar]);

        var decisoes = Enumerable.Range(0, 4).Select(_ => controle.Decidir(TipoOperacao.Cobranca).Comportamento).ToList();

        decisoes.Should().Equal(Comportamento.ErroInterno, Comportamento.Recusar, Comportamento.Indisponivel, Comportamento.Indisponivel);
        controle.TamanhoRoteiro.Should().Be(0);
    }

    [Fact]
    public void Desfazer_o_modo_volta_ao_sorteio()
    {
        var controle = Controle();
        controle.ForcarModo(Comportamento.SemResposta);
        controle.Decidir(TipoOperacao.Cobranca).Should().Be(new DecisaoComportamento(Comportamento.SemResposta, 0, Sorteado: false));

        controle.DesfazerModo();

        controle.Decidir(TipoOperacao.Cobranca).Should().Be(new DecisaoComportamento(Comportamento.Aprovar, 0, Sorteado: true));
    }

    [Fact]
    public void Restaurar_volta_a_configuracao_sem_modo_e_sem_roteiro()
    {
        var controle = Controle();
        controle.ForcarModo(Comportamento.Recusar);
        controle.Enfileirar([Comportamento.Indisponivel]);
        controle.Alterar(p => p.LatenciaMinimaMs = p.LatenciaMaximaMs = 500).Should().BeEmpty();

        controle.Restaurar();

        controle.ModoForcado.Should().BeNull();
        controle.TamanhoRoteiro.Should().Be(0);
        controle.Decidir(TipoOperacao.Cobranca).Should().Be(new DecisaoComportamento(Comportamento.Aprovar, 0, Sorteado: true));
    }

    [Fact]
    public void A_latencia_definida_pelo_controle_vale_no_modo_deterministico()
    {
        // Research R-03: o modo determinístico zera só os valores da partida (FR-024, latência alta).
        var controle = Controle();

        controle.Alterar(p => (p.LatenciaMinimaMs, p.LatenciaMaximaMs) = (180, 180)).Should().BeEmpty();

        controle.Decidir(TipoOperacao.Cobranca).LatenciaMs.Should().Be(180);
    }

    [Fact]
    public void Alteracao_invalida_nao_muda_nada()
    {
        var controle = Controle();

        controle.Alterar(p => p.IndisponivelPercentual = 101).Should().NotBeEmpty();

        controle.Parametros.IndisponivelPercentual.Should().Be(0);
    }

    [Fact]
    public void O_estorno_sorteado_nunca_e_recusado()
    {
        var parametros = new OpcoesSimulacao { TaxaAprovacaoPercentual = 0, IndisponivelPercentual = 0, LimiteTaxaPercentual = 0, ErroInternoPercentual = 0, SemRespostaPercentual = 0, ProcessarSemResponderPercentual = 0 };
        var aleatorio = new Random(1);

        Enumerable.Range(0, 500).Select(_ => DecisorComportamento.Sortear(parametros, TipoOperacao.Estorno, aleatorio))
            .Should().OnlyContain(c => c == Comportamento.Aprovar);
        Enumerable.Range(0, 500).Select(_ => DecisorComportamento.Sortear(parametros, TipoOperacao.Cobranca, aleatorio))
            .Should().OnlyContain(c => c == Comportamento.Recusar);
    }

    [Fact]
    public void Com_semente_o_sorteio_se_repete()
    {
        static List<DecisaoComportamento> Sequencia() =>
            [.. Enumerable.Range(0, 100).Select(_ => new ControleSimulacao(Options.Create(new OpcoesSimulacao { Semente = 42 })))
                .Take(1).SelectMany(c => Enumerable.Range(0, 100).Select(_ => c.Decidir(TipoOperacao.Cobranca)))];

        Sequencia().Should().Equal(Sequencia());
    }

    [Theory]
    [InlineData("aprovar", Comportamento.Aprovar)]
    [InlineData("recusar", Comportamento.Recusar)]
    [InlineData("indisponivel", Comportamento.Indisponivel)]
    [InlineData("limite-taxa", Comportamento.LimiteTaxa)]
    [InlineData("erro-interno", Comportamento.ErroInterno)]
    [InlineData("sem-resposta", Comportamento.SemResposta)]
    [InlineData("processar-sem-responder", Comportamento.ProcessarSemResponder)]
    public void Os_nomes_do_contrato_vao_e_voltam(string nome, Comportamento comportamento)
    {
        NomesComportamento.TentarLer(nome, out var lido).Should().BeTrue();
        lido.Should().Be(comportamento);
        comportamento.Nome().Should().Be(nome);
        NomesComportamento.TentarLer("fora-do-ar", out _).Should().BeFalse();
    }
}
