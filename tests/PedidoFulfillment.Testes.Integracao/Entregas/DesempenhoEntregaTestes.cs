using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Entregas.CenarioEntrega;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Entregas;

/// <summary>
/// SC-001 e SC-005 do REQ-10, pelos instantes gravados no banco, com o Worker completo: do
/// pagamento aprovado e da criação do pedido até a publicação de <c>entrega.criada</c>.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class DesempenhoEntregaTestes(MensageriaTeste mensageria)
{
    private const int Aquecimento = 3;
    private const int Medicoes = 20;
    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Noventa_e_cinco_por_cento_das_entregas_saem_em_ate_5_s_do_pagamento_e_10_s_do_pedido()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, stringConexao);

        var doPagamento = new List<long>();
        var doPedido = new List<long>();
        for (var i = 0; i < Aquecimento + Medicoes; i++)
        {
            var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
            (await MensageriaTeste.EsperarAteAsync(async () => await EntregaEnviadaAsync(stringConexao, pedidoId), Prazo))
                .Should().BeTrue($"pedido {i + 1}: entrega.criada publicado");
            if (i >= Aquecimento)
            {
                doPagamento.Add(await MilissegundosAteEntregaAsync(stringConexao, pedidoId, "(SELECT ProcessadoEm FROM Pagamentos WHERE PedidoId = '{0}')"));
                doPedido.Add(await MilissegundosAteEntregaAsync(stringConexao, pedidoId, "(SELECT CriadoEm FROM Pedidos WHERE Id = '{0}')"));
            }
        }

        // Percentil 95 de 20 medições: a 19ª em ordem crescente.
        var p95Pagamento = doPagamento.Order().ElementAt(18);
        var p95Pedido = doPedido.Order().ElementAt(18);
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"p95 do pagamento à entrega: {p95Pagamento} ms; p95 do pedido à entrega: {p95Pedido} ms; " +
            $"máximos: {doPagamento.Max()} ms e {doPedido.Max()} ms.");
        p95Pagamento.Should().BeLessThan(5_000, "SC-001");
        p95Pedido.Should().BeLessThan(10_000, "SC-005");
    }

    private static async Task<bool> EntregaEnviadaAsync(string stringConexao, Guid pedidoId) =>
        await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM EventosSaida WHERE Tipo = '{TiposEvento.EntregaCriada}' AND EnviadoEm IS NOT NULL AND Conteudo LIKE '%{pedidoId}%'") == 1;

    /// <summary>Do instante dado (subconsulta com <c>{0}</c> no lugar do pedido) até o envio de <c>entrega.criada</c>.</summary>
    private static Task<long> MilissegundosAteEntregaAsync(string stringConexao, Guid pedidoId, string inicio) =>
        BancoDadosTeste.ConsultarAsync<long>(stringConexao,
            $"SELECT DATEDIFF_BIG(millisecond, {string.Format(System.Globalization.CultureInfo.InvariantCulture, inicio, pedidoId)}, EnviadoEm) " +
            $"FROM EventosSaida WHERE Tipo = '{TiposEvento.EntregaCriada}' AND Conteudo LIKE '%{pedidoId}%'");
}
