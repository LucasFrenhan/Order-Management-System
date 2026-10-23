using FluentAssertions;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Testes.Unitarios.Configuracao;

public sealed class OpcoesGatewayPagamentoTestes
{
    private static readonly ValidadorOpcoesGatewayPagamento Validador = new();

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(100d)]
    [InlineData(37.5)]
    public void Taxas_de_0_a_100_sao_validas(double? taxa)
    {
        var opcoes = taxa is { } valor ? new OpcoesGatewayPagamento { TaxaAprovacaoPercentual = valor } : new OpcoesGatewayPagamento();

        Validador.Validate(null, opcoes).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(100.5)]
    [InlineData(150d)]
    [InlineData(double.NaN)]
    public void Taxa_fora_de_0_a_100_falha_nomeando_a_chave_sem_o_valor(double taxa)
    {
        // Texto fixo: o valor configurado nunca aparece na mensagem.
        Validador.Validate(null, new OpcoesGatewayPagamento { TaxaAprovacaoPercentual = taxa }).Failures.Should()
            .Equal("GatewayPagamento:TaxaAprovacaoPercentual deve estar entre 0 e 100.");
    }
}
