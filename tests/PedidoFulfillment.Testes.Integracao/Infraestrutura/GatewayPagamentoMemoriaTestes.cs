using FluentAssertions;
using PedidoFulfillment.Application.Pagamentos;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>Gateway em memória dos testes (REQ-09, R-02; REQ-19, R-11), com semente fixa: nenhum teste depende de sorte.</summary>
public sealed class GatewayPagamentoMemoriaTestes
{
    private const int Semente = 20261023;

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static GatewayPagamentoMemoria Gateway(double? taxa = null) =>
        new(taxa ?? 90, new Random(Semente));

    private static async Task<List<ResultadoCobranca>> CobrarAsync(GatewayPagamentoMemoria gateway, int vezes)
    {
        var resultados = new List<ResultadoCobranca>(vezes);
        for (var i = 0; i < vezes; i++)
        {
            resultados.Add(await gateway.CobrarAsync(Guid.CreateVersion7(), 100m, Cancelamento));
        }

        return resultados;
    }

    [Fact]
    public async Task Com_a_taxa_de_90_cerca_de_90_por_cento_sao_aprovadas()
    {
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
        resultado.Motivo.Should().Be(GatewayPagamentoMemoria.MotivoRecusa).And.Be("Pagamento recusado pelo emissor.");
        resultado.IdentificadorTransacao.Should().BeNull();
    }

    [Fact]
    public async Task A_mesma_cobranca_repetida_devolve_o_mesmo_resultado_sem_novo_sorteio()
    {
        // REQ-13, FR-014: o pedido é a chave de idempotência da cobrança.
        var gateway = Gateway(50);
        var pedidos = Enumerable.Range(0, 200).Select(_ => Guid.CreateVersion7()).ToList();
        var primeiras = new List<ResultadoCobranca>();
        foreach (var pedidoId in pedidos)
        {
            primeiras.Add(await gateway.CobrarAsync(pedidoId, 10m, Cancelamento));
        }

        for (var i = 0; i < pedidos.Count; i++)
        {
            (await gateway.CobrarAsync(pedidos[i], 10m, Cancelamento)).Should().Be(primeiras[i]);
        }

        primeiras.Should().Contain(r => r.Aprovado).And.Contain(r => !r.Aprovado, "pedidos diferentes sorteiam separadamente");
    }

    [Fact]
    public async Task O_mesmo_estorno_repetido_devolve_o_mesmo_identificador()
    {
        // REQ-13, FR-013.
        var gateway = Gateway();
        var pedidoId = Guid.CreateVersion7();

        var primeiro = await gateway.EstornarAsync(pedidoId, "SIM-1", 10m, Cancelamento);
        var segundo = await gateway.EstornarAsync(pedidoId, "SIM-1", 10m, Cancelamento);
        var outroPedido = await gateway.EstornarAsync(Guid.CreateVersion7(), "SIM-2", 10m, Cancelamento);

        segundo.Should().Be(primeiro);
        outroPedido.IdentificadorEstorno.Should().NotBe(primeiro.IdentificadorEstorno);
    }

    [Fact]
    public async Task Chamadas_simultaneas_para_o_mesmo_pedido_devolvem_um_unico_resultado()
    {
        var gateway = new GatewayPagamentoMemoria();
        var pedidoId = Guid.CreateVersion7();

        var cobrancas = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => gateway.CobrarAsync(pedidoId, 10m, Cancelamento), Cancelamento)));
        var estornos = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => gateway.EstornarAsync(pedidoId, "SIM-1", 10m, Cancelamento), Cancelamento)));

        cobrancas.Select(c => c.IdentificadorTransacao).Distinct().Should().ContainSingle();
        estornos.Select(e => e.IdentificadorEstorno).Distinct().Should().ContainSingle();
    }

    [Fact]
    public async Task A_consulta_devolve_a_cobranca_guardada_ou_nao_encontrada()
    {
        // REQ-19, FR-015: a mesma consulta pela chave que o gateway por HTTP oferece.
        var aprovadora = Gateway(100);
        var recusadora = Gateway(0);
        var pedidoId = Guid.CreateVersion7();

        (await aprovadora.ConsultarCobrancaAsync(pedidoId, Cancelamento)).Should().Be(ConsultaCobranca.NaoEncontrada);
        var cobranca = await aprovadora.CobrarAsync(pedidoId, 10m, Cancelamento);
        await recusadora.CobrarAsync(pedidoId, 10m, Cancelamento);

        (await aprovadora.ConsultarCobrancaAsync(pedidoId, Cancelamento)).Should().Be(ConsultaCobranca.Aprovada(cobranca.IdentificadorTransacao!));
        (await recusadora.ConsultarCobrancaAsync(pedidoId, Cancelamento)).Situacao.Should().Be(SituacaoConsultaCobranca.Recusada);
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
