using FluentAssertions;
using PedidoFulfillment.Application.Pagamentos;

namespace PedidoFulfillment.Testes.Unitarios.Pagamentos;

/// <summary>Último erro do estorno só por categoria (REQ-28, R-05).</summary>
public sealed class DescricaoFalhaEstornoTestes
{
    [Fact]
    public void A_recusa_leva_o_motivo_do_gateway() =>
        DescricaoFalhaEstorno.Recusado("Cobrança desconhecida.").Should().Be("Recusado: Cobrança desconhecida.");

    [Fact]
    public void A_recusa_corta_o_motivo_em_180_caracteres()
    {
        var texto = DescricaoFalhaEstorno.Recusado(new string('m', 300));

        texto.Should().Be("Recusado: " + new string('m', 180));
        texto.Length.Should().BeLessThanOrEqualTo(200);
    }

    [Fact]
    public void A_recusa_sem_motivo_tem_texto_proprio() =>
        DescricaoFalhaEstorno.Recusado("  ").Should().Be("Recusado: sem motivo informado.");

    [Theory]
    [InlineData(TipoFalhaGateway.Passageira, "Falha passageira do gateway.")]
    [InlineData(TipoFalhaGateway.Permanente, "Falha permanente do gateway.")]
    public void A_falha_do_gateway_vira_so_a_categoria(TipoFalhaGateway tipo, string esperado)
    {
        var excecao = new FalhaGatewayPagamentoException(
            tipo, OperacaoGateway.Estorno, "https://pagamentos.interno/v1/estornos/PAG-0199f1a1 respondeu 503");

        var texto = DescricaoFalhaEstorno.Falha(excecao);

        texto.Should().Be(esperado);
        texto.Should().NotContain("PAG-").And.NotContain("http");
    }

    [Fact]
    public void Outra_excecao_vira_so_o_nome_do_tipo()
    {
        var excecao = new InvalidOperationException("Chave sk_live_123 recusada em https://pagamentos.interno para EST-0199.");

        var texto = DescricaoFalhaEstorno.Falha(excecao);

        texto.Should().Be("Erro inesperado: InvalidOperationException.");
    }
}
