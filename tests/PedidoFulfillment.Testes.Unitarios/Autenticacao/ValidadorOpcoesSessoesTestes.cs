using FluentAssertions;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Testes.Unitarios.Autenticacao;

/// <summary>Validação das opções na partida (REQ-24, FR-005, contrato › Configuração).</summary>
public sealed class ValidadorOpcoesSessoesTestes
{
    private static string[] Falhas(OpcoesSessoes opcoes)
    {
        var resultado = new ValidadorOpcoesSessoes().Validate(null, opcoes);
        return resultado.Failed ? [.. resultado.Failures!] : [];
    }

    [Fact]
    public void Os_padroes_sao_validos()
    {
        var opcoes = new OpcoesSessoes();

        Falhas(opcoes).Should().BeEmpty();
        (opcoes.ValidadeTokenRenovacaoDias, opcoes.DuracaoMaximaDias).Should().Be((14, 30));
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(91, 365)]
    public void Validade_fora_da_faixa_e_recusada(int validade, int duracao) =>
        Falhas(new OpcoesSessoes { ValidadeTokenRenovacaoDias = validade, DuracaoMaximaDias = duracao })
            .Should().Contain(f => f.Contains("Sessoes:ValidadeTokenRenovacaoDias"));

    [Theory]
    [InlineData(0)]
    [InlineData(366)]
    public void Duracao_fora_da_faixa_e_recusada(int duracao) =>
        Falhas(new OpcoesSessoes { ValidadeTokenRenovacaoDias = 1, DuracaoMaximaDias = duracao })
            .Should().Contain(f => f.Contains("Sessoes:DuracaoMaximaDias"));

    [Fact]
    public void Duracao_menor_que_a_validade_do_token_e_recusada() =>
        Falhas(new OpcoesSessoes { ValidadeTokenRenovacaoDias = 14, DuracaoMaximaDias = 13 })
            .Should().ContainSingle().Which.Should().Contain("DuracaoMaximaDias").And.Contain("ValidadeTokenRenovacaoDias");

    [Fact]
    public void A_configuracao_pronta_traz_as_validades()
    {
        var configuracao = new OpcoesSessoes().ParaConfiguracao();

        configuracao.ValidadeToken.Should().Be(TimeSpan.FromDays(14));
        configuracao.DuracaoMaxima.Should().Be(TimeSpan.FromDays(30));
    }
}
