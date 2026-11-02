using FluentAssertions;
using PedidoFulfillment.PagamentosSimulados.Operacoes;

namespace PedidoFulfillment.Testes.Unitarios.PagamentosSimulados;

/// <summary>Memória idempotente da API simulada (REQ-19, FR-005, research R-04; data-model › transições).</summary>
public sealed class ArmazenamentoOperacoesTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_primeira_chamada_cria_a_operacao_em_andamento_e_a_repeticao_devolve_a_mesma()
    {
        var armazenamento = new ArmazenamentoOperacoes();
        var chave = Guid.CreateVersion7();

        var primeira = armazenamento.Registrar(TipoOperacao.Cobranca, chave, 10m);
        var repeticao = armazenamento.Registrar(TipoOperacao.Cobranca, chave, 10m);

        primeira.Nova.Should().BeTrue();
        primeira.Operacao.Situacao.Should().Be(SituacaoOperacao.EmAndamento);
        repeticao.Nova.Should().BeFalse();
        repeticao.Operacao.Should().BeSameAs(primeira.Operacao);
        repeticao.Operacao.Chamadas.Should().Be(2);
        repeticao.Operacao.Efetivacoes.Should().Be(0);
    }

    [Fact]
    public void Cobranca_e_estorno_tem_espacos_de_chave_separados()
    {
        var armazenamento = new ArmazenamentoOperacoes();
        var chave = Guid.CreateVersion7();

        armazenamento.Registrar(TipoOperacao.Cobranca, chave, 10m).Nova.Should().BeTrue();
        armazenamento.Registrar(TipoOperacao.Estorno, chave, 10m, "PAG-1").Nova.Should().BeTrue();
    }

    [Fact]
    public void Efetivar_gera_identificador_PAG_ou_EST_e_conta_uma_unica_efetivacao()
    {
        var armazenamento = new ArmazenamentoOperacoes();
        var cobranca = armazenamento.Registrar(TipoOperacao.Cobranca, Guid.CreateVersion7(), 10m).Operacao;
        var estorno = armazenamento.Registrar(TipoOperacao.Estorno, Guid.CreateVersion7(), 10m, "PAG-1").Operacao;

        cobranca.Efetivar(Agora);
        estorno.Efetivar(Agora);

        cobranca.Situacao.Should().Be(SituacaoOperacao.Aprovada);
        cobranca.Identificador.Should().MatchRegex("^PAG-[0-9a-f]{32}$");
        estorno.Situacao.Should().Be(SituacaoOperacao.Confirmado);
        estorno.Identificador.Should().MatchRegex("^EST-[0-9a-f]{32}$");
        cobranca.Efetivacoes.Should().Be(1);
        FluentActions.Invoking(() => cobranca.Efetivar(Agora)).Should().Throw<InvalidOperationException>();
        FluentActions.Invoking(() => cobranca.Recusar("x", Agora)).Should().Throw<InvalidOperationException>();
        cobranca.Efetivacoes.Should().Be(1, "SC-002: nunca passa de 1");
    }

    [Fact]
    public void A_recusa_tambem_e_uma_decisao_guardada_sem_identificador()
    {
        var operacao = new ArmazenamentoOperacoes().Registrar(TipoOperacao.Cobranca, Guid.CreateVersion7(), 10m).Operacao;

        operacao.Recusar("Pagamento recusado pelo emissor.", Agora);

        (operacao.Situacao, operacao.Identificador, operacao.Motivo, operacao.Efetivacoes)
            .Should().Be((SituacaoOperacao.Recusada, (string?)null, "Pagamento recusado pelo emissor.", 1));
    }

    [Fact]
    public void Liberar_remove_so_a_operacao_em_andamento_nunca_a_concluida()
    {
        var armazenamento = new ArmazenamentoOperacoes();
        var emAndamento = armazenamento.Registrar(TipoOperacao.Cobranca, Guid.CreateVersion7(), 10m).Operacao;
        var concluida = armazenamento.Registrar(TipoOperacao.Cobranca, Guid.CreateVersion7(), 10m).Operacao;
        concluida.Efetivar(Agora);

        armazenamento.Liberar(emAndamento).Should().BeTrue();
        armazenamento.Liberar(concluida).Should().BeFalse();

        armazenamento.Obter(TipoOperacao.Cobranca, emAndamento.Chave).Should().BeNull();
        armazenamento.Obter(TipoOperacao.Cobranca, concluida.Chave).Should().BeSameAs(concluida);
    }

    [Fact]
    public void Falha_sobre_uma_chave_concluida_so_conta_a_chamada()
    {
        // Research R-03: a falha simulada nunca remove nem altera uma operação concluída.
        var armazenamento = new ArmazenamentoOperacoes();
        var operacao = armazenamento.Registrar(TipoOperacao.Cobranca, Guid.CreateVersion7(), 10m).Operacao;
        operacao.Efetivar(Agora);
        var identificador = operacao.Identificador;

        armazenamento.ContarChamada(TipoOperacao.Cobranca, operacao.Chave);
        armazenamento.ContarChamada(TipoOperacao.Cobranca, Guid.CreateVersion7());

        operacao.Chamadas.Should().Be(2);
        (operacao.Situacao, operacao.Identificador, operacao.Efetivacoes).Should().Be((SituacaoOperacao.Aprovada, identificador, 1));
    }

    [Fact]
    public void A_cobranca_aprovada_e_encontrada_pelo_identificador()
    {
        var armazenamento = new ArmazenamentoOperacoes();
        var aprovada = armazenamento.Registrar(TipoOperacao.Cobranca, Guid.CreateVersion7(), 10m).Operacao;
        aprovada.Efetivar(Agora);
        var recusada = armazenamento.Registrar(TipoOperacao.Cobranca, Guid.CreateVersion7(), 10m).Operacao;
        recusada.Recusar("x", Agora);

        armazenamento.CobrancaAprovada(aprovada.Identificador!).Should().BeSameAs(aprovada);
        armazenamento.CobrancaAprovada("PAG-desconhecido").Should().BeNull();
    }

    [Fact]
    public async Task Chamadas_simultaneas_com_a_mesma_chave_criam_uma_unica_operacao()
    {
        var armazenamento = new ArmazenamentoOperacoes();
        var chave = Guid.CreateVersion7();

        var registros = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ =>
            Task.Run(() => armazenamento.Registrar(TipoOperacao.Cobranca, chave, 10m), TestContext.Current.CancellationToken)));

        registros.Count(r => r.Nova).Should().Be(1);
        registros.Select(r => r.Operacao).Distinct().Should().ContainSingle().Which.Chamadas.Should().Be(64);
    }

    [Fact]
    public void Limpar_apaga_tudo()
    {
        var armazenamento = new ArmazenamentoOperacoes();
        var chave = Guid.CreateVersion7();
        armazenamento.Registrar(TipoOperacao.Cobranca, chave, 10m);

        armazenamento.Limpar();

        armazenamento.Obter(TipoOperacao.Cobranca, chave).Should().BeNull();
    }
}
