using FluentAssertions;
using PedidoFulfillment.PagamentosSimulados.Simulacao;

namespace PedidoFulfillment.Testes.Unitarios.PagamentosSimulados;

/// <summary>Opções da API simulada (REQ-19, research R-03 e R-05; contrato da API › Configuração).</summary>
public sealed class OpcoesPagamentosSimuladosTestes
{
    private const string ChaveApi = "chave-api-secreta-1234";
    private const string ChaveControle = "chave-controle-secreta-1234";

    [Fact]
    public void Os_padroes_da_simulacao_sao_validos_e_seguem_o_contrato()
    {
        var opcoes = new OpcoesSimulacao();

        opcoes.Validar().Should().BeEmpty();
        (opcoes.LatenciaMinimaMs, opcoes.LatenciaMaximaMs, opcoes.CaudaPercentual, opcoes.CaudaMinimoMs, opcoes.CaudaMaximoMs).Should().Be((40, 250, 5d, 1000, 5000));
        (opcoes.TaxaAprovacaoPercentual, opcoes.IndisponivelPercentual, opcoes.LimiteTaxaPercentual, opcoes.ErroInternoPercentual).Should().Be((90d, 2d, 1d, 1d));
        (opcoes.SemRespostaPercentual, opcoes.ProcessarSemResponderPercentual, opcoes.RetryAfterSegundos, opcoes.SemRespostaMs).Should().Be((1d, 1d, 1, 15000));
        opcoes.Deterministico.Should().BeFalse();
    }

    [Theory]
    [InlineData(nameof(OpcoesSimulacao.LatenciaMinimaMs), -1)]
    [InlineData(nameof(OpcoesSimulacao.LatenciaMaximaMs), 60001)]
    [InlineData(nameof(OpcoesSimulacao.CaudaMinimoMs), -1)]
    [InlineData(nameof(OpcoesSimulacao.CaudaMaximoMs), 60001)]
    [InlineData(nameof(OpcoesSimulacao.RetryAfterSegundos), 0)]
    [InlineData(nameof(OpcoesSimulacao.RetryAfterSegundos), 61)]
    [InlineData(nameof(OpcoesSimulacao.SemRespostaMs), 99)]
    [InlineData(nameof(OpcoesSimulacao.SemRespostaMs), 120001)]
    public void Inteiros_fora_da_faixa_falham_nomeando_a_chave(string propriedade, int valor)
    {
        var opcoes = new OpcoesSimulacao();
        typeof(OpcoesSimulacao).GetProperty(propriedade)!.SetValue(opcoes, valor);

        opcoes.Validar().Should().Contain(erro => erro.StartsWith($"Simulacao:{propriedade}", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(nameof(OpcoesSimulacao.CaudaPercentual))]
    [InlineData(nameof(OpcoesSimulacao.TaxaAprovacaoPercentual))]
    [InlineData(nameof(OpcoesSimulacao.IndisponivelPercentual))]
    [InlineData(nameof(OpcoesSimulacao.LimiteTaxaPercentual))]
    [InlineData(nameof(OpcoesSimulacao.ErroInternoPercentual))]
    [InlineData(nameof(OpcoesSimulacao.SemRespostaPercentual))]
    [InlineData(nameof(OpcoesSimulacao.ProcessarSemResponderPercentual))]
    public void Percentuais_fora_de_0_a_100_falham_nomeando_a_chave(string propriedade)
    {
        foreach (var valor in new[] { -0.1, 100.1, double.NaN })
        {
            var opcoes = new OpcoesSimulacao();
            typeof(OpcoesSimulacao).GetProperty(propriedade)!.SetValue(opcoes, valor);

            opcoes.Validar().Should().Contain($"Simulacao:{propriedade} deve estar entre 0 e 100.");
        }
    }

    [Fact]
    public void Minimo_maior_que_o_maximo_falha()
    {
        new OpcoesSimulacao { LatenciaMinimaMs = 300, LatenciaMaximaMs = 200 }.Validar().Should().ContainSingle()
            .Which.Should().StartWith("Simulacao:LatenciaMinimaMs não pode passar");
        new OpcoesSimulacao { CaudaMinimoMs = 3000, CaudaMaximoMs = 2000 }.Validar().Should().ContainSingle()
            .Which.Should().StartWith("Simulacao:CaudaMinimoMs não pode passar");
    }

    [Fact]
    public void A_soma_das_chances_de_falha_nao_pode_passar_de_100()
    {
        var opcoes = new OpcoesSimulacao { IndisponivelPercentual = 60, ErroInternoPercentual = 41 };

        opcoes.Validar().Should().ContainSingle().Which.Should().Be("A soma das chances de falha de Simulacao não pode passar de 100.");
    }

    [Fact]
    public void O_validador_da_partida_aceita_os_padroes_e_recusa_a_violacao()
    {
        var validador = new ValidadorOpcoesSimulacao();

        validador.Validate(null, new OpcoesSimulacao()).Succeeded.Should().BeTrue();
        validador.Validate(null, new OpcoesSimulacao { RetryAfterSegundos = 0 }).Failed.Should().BeTrue();
    }

    [Fact]
    public void Sem_semente_o_sorteio_e_aleatorio_e_nome_nulo_nao_e_comportamento()
    {
        new ControleSimulacao(Microsoft.Extensions.Options.Options.Create(new OpcoesSimulacao { Semente = null })).Parametros.Should().NotBeNull();
        NomesComportamento.TentarLer(null, out _).Should().BeFalse();
    }

    [Fact]
    public void Chaves_validas_passam()
    {
        new ValidadorOpcoesSeguranca().Validate(null, new OpcoesSeguranca { ChaveApi = ChaveApi, ChaveControle = ChaveControle })
            .Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData("", ChaveControle, "Seguranca:ChaveApi é obrigatória.")]
    [InlineData(ChaveApi, " ", "Seguranca:ChaveControle é obrigatória.")]
    [InlineData("curta-demais", ChaveControle, "Seguranca:ChaveApi deve ter pelo menos 16 caracteres.")]
    [InlineData(ChaveApi, "curta-demais", "Seguranca:ChaveControle deve ter pelo menos 16 caracteres.")]
    [InlineData(ChaveApi, ChaveApi, "Seguranca:ChaveControle deve ser diferente de Seguranca:ChaveApi.")]
    public void Chaves_ausentes_curtas_ou_iguais_falham_sem_mostrar_o_valor(string chaveApi, string chaveControle, string esperado)
    {
        var falhas = new ValidadorOpcoesSeguranca().Validate(null, new OpcoesSeguranca { ChaveApi = chaveApi, ChaveControle = chaveControle }).Failures;

        falhas.Should().Equal(esperado);
        falhas!.Single().Should().NotContain("secreta").And.NotContain("curta-demais");
    }
}
