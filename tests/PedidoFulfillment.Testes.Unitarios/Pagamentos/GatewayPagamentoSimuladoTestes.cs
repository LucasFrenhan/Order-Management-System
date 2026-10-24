using FluentAssertions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Pagamentos;

namespace PedidoFulfillment.Testes.Unitarios.Pagamentos;

/// <summary>Gateway simulado (REQ-09, R-02), com semente fixa: nenhum teste depende de sorte.</summary>
public sealed class GatewayPagamentoSimuladoTestes
{
    private const int Semente = 20261023;

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static GatewayPagamentoSimulado Gateway(double? taxa = null) =>
        new(Options.Create(taxa is { } valor ? new OpcoesGatewayPagamento { TaxaAprovacaoPercentual = valor } : new OpcoesGatewayPagamento()), new Random(Semente));

    private static async Task<List<Application.Pagamentos.ResultadoCobranca>> CobrarAsync(GatewayPagamentoSimulado gateway, int vezes)
    {
        var resultados = new List<Application.Pagamentos.ResultadoCobranca>(vezes);
        for (var i = 0; i < vezes; i++)
        {
            resultados.Add(await gateway.CobrarAsync(Guid.CreateVersion7(), 100m, Cancelamento));
        }

        return resultados;
    }

    [Fact]
    public async Task Com_a_taxa_padrao_cerca_de_90_por_cento_sao_aprovadas()
    {
        new OpcoesGatewayPagamento().TaxaAprovacaoPercentual.Should().Be(90);

        var resultados = await CobrarAsync(Gateway(), 10_000);

        var aprovadas = resultados.Count(r => r.Aprovado) / 10_000d;
        aprovadas.Should().BeInRange(0.88, 0.92, "SC-002");
    }

    [Theory]
    [InlineData(100, true)]
    [InlineData(0, false)]
    public async Task Taxa_100_aprova_tudo_e_taxa_0_recusa_tudo(double taxa, bool aprovado)
    {
        var resultados = await CobrarAsync(Gateway(taxa), 1_000);

        resultados.Should().OnlyContain(r => r.Aprovado == aprovado);
    }

    [Fact]
    public async Task Aprovada_tem_identificador_SIM_unico_de_36_caracteres_e_nenhum_motivo()
    {
        var resultados = await CobrarAsync(Gateway(100), 1_000);

        resultados.Should().OnlyContain(r => r.IdentificadorTransacao!.StartsWith("SIM-", StringComparison.Ordinal)
            && r.IdentificadorTransacao.Length == 36 && r.Motivo == null);
        resultados.Select(r => r.IdentificadorTransacao).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Recusada_traz_o_motivo_padrao_e_nenhum_identificador()
    {
        var resultado = await Gateway(0).CobrarAsync(Guid.CreateVersion7(), 10m, Cancelamento);

        resultado.Aprovado.Should().BeFalse();
        resultado.Motivo.Should().Be(GatewayPagamentoSimulado.MotivoRecusa).And.Be("Pagamento recusado pelo emissor.");
        resultado.IdentificadorTransacao.Should().BeNull();
    }

    [Fact]
    public async Task Estorno_simulado_sempre_confirma_com_identificador_SIM_EST()
    {
        var resultado = await Gateway(0).EstornarAsync(Guid.CreateVersion7(), "SIM-1", 10m, Cancelamento);

        resultado.Confirmado.Should().BeTrue("REQ-13, R-03: o estorno simulado sempre confirma");
        resultado.IdentificadorEstorno.Should().StartWith("SIM-EST-").And.HaveLength(40);
        resultado.IdentificadorEstorno!.Length.Should().BeLessThanOrEqualTo(100);
        resultado.Motivo.Should().BeNull();
    }
}
