using FluentAssertions;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Testes.Unitarios.Configuracao;

/// <summary>Opções do cliente do gateway (REQ-19, FR-018; contrato do cliente › Configuração).</summary>
public sealed class OpcoesGatewayPagamentoTestes
{
    private const string ChaveSecreta = "chave-api-muito-secreta-123";

    private static readonly ValidadorOpcoesGatewayPagamento Validador = new();

    private static OpcoesGatewayPagamento Validas() => new() { UrlBase = "http://pagamentos:8080", ChaveApi = ChaveSecreta };

    [Fact]
    public void Os_padroes_com_url_e_chave_sao_validos_e_seguem_o_contrato()
    {
        var opcoes = Validas();

        Validador.Validate(null, opcoes).Succeeded.Should().BeTrue();
        (opcoes.TempoTentativaMs, opcoes.Tentativas, opcoes.EsperaBaseMs, opcoes.TempoTotalMs).Should().Be((3000, 3, 300, 10000));
        (opcoes.Circuito.ProporcaoFalhas, opcoes.Circuito.MinimoChamadas, opcoes.Circuito.JanelaSegundos, opcoes.Circuito.PausaSegundos)
            .Should().Be((0.5, 6, 30, 30));
    }

    [Fact]
    public void A_taxa_de_aprovacao_nao_e_mais_do_Worker()
    {
        typeof(OpcoesGatewayPagamento).GetProperty("TaxaAprovacaoPercentual").Should().BeNull("a taxa agora é da API simulada (REQ-19)");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("pagamentos:8080")]
    [InlineData("ftp://pagamentos")]
    [InlineData("/v1")]
    public void UrlBase_ausente_ou_invalida_falha_nomeando_a_chave(string? url)
    {
        var opcoes = Validas();
        opcoes.UrlBase = url;

        Validador.Validate(null, opcoes).Failures.Should().ContainSingle()
            .Which.Should().StartWith("GatewayPagamento:UrlBase");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("curta-secreta")]
    public void ChaveApi_ausente_ou_curta_falha_sem_mostrar_o_valor(string? chave)
    {
        var opcoes = Validas();
        opcoes.ChaveApi = chave;

        var falhas = Validador.Validate(null, opcoes).Failures;

        falhas.Should().ContainSingle().Which.Should().StartWith("GatewayPagamento:ChaveApi");
        if (!string.IsNullOrEmpty(chave))
        {
            falhas!.Single().Should().NotContain(chave);
        }
    }

    [Theory]
    [InlineData(nameof(OpcoesGatewayPagamento.TempoTentativaMs), 99)]
    [InlineData(nameof(OpcoesGatewayPagamento.TempoTentativaMs), 30001)]
    [InlineData(nameof(OpcoesGatewayPagamento.Tentativas), 0)]
    [InlineData(nameof(OpcoesGatewayPagamento.Tentativas), 7)]
    [InlineData(nameof(OpcoesGatewayPagamento.EsperaBaseMs), 9)]
    [InlineData(nameof(OpcoesGatewayPagamento.EsperaBaseMs), 10001)]
    [InlineData(nameof(OpcoesGatewayPagamento.TempoTotalMs), 3000)]
    [InlineData(nameof(OpcoesGatewayPagamento.TempoTotalMs), 60001)]
    public void Tempos_e_tentativas_fora_da_faixa_falham_nomeando_a_chave(string propriedade, int valor)
    {
        var opcoes = Validas();
        typeof(OpcoesGatewayPagamento).GetProperty(propriedade)!.SetValue(opcoes, valor);

        // Uma tentativa maior que o tempo total também quebra a regra do total; basta nomear a chave.
        var falhas = Validador.Validate(null, opcoes).Failures;
        falhas.Should().Contain(f => f.StartsWith($"GatewayPagamento:{propriedade}", StringComparison.Ordinal));
        falhas.Should().NotContain(f => f.Contains(valor.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0.09, 6, 30, 30, "ProporcaoFalhas")]
    [InlineData(1.01, 6, 30, 30, "ProporcaoFalhas")]
    [InlineData(0.5, 1, 30, 30, "MinimoChamadas")]
    [InlineData(0.5, 101, 30, 30, "MinimoChamadas")]
    [InlineData(0.5, 6, 0, 30, "JanelaSegundos")]
    [InlineData(0.5, 6, 601, 30, "JanelaSegundos")]
    [InlineData(0.5, 6, 30, 0, "PausaSegundos")]
    [InlineData(0.5, 6, 30, 601, "PausaSegundos")]
    public void Circuito_fora_da_faixa_falha_nomeando_a_chave(double proporcao, int minimo, int janela, int pausa, string chave)
    {
        var opcoes = Validas();
        opcoes.Circuito = new OpcoesCircuitoGateway { ProporcaoFalhas = proporcao, MinimoChamadas = minimo, JanelaSegundos = janela, PausaSegundos = pausa };

        Validador.Validate(null, opcoes).Failures.Should().ContainSingle().Which.Should().StartWith($"GatewayPagamento:Circuito:{chave}");
    }
}
