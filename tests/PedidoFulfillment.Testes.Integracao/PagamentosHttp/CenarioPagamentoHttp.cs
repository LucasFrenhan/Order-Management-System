using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Entregas;
using PedidoFulfillment.PagamentosSimulados.Simulacao;
using PedidoFulfillment.Testes.Integracao.Entregas;
using PedidoFulfillment.Testes.Integracao.Estoque;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Pagamentos;

namespace PedidoFulfillment.Testes.Integracao.PagamentosHttp;

/// <summary>
/// Atalhos dos testes do gateway por HTTP (REQ-19, FR-024): Worker de estoque e pagamento chamando
/// a API simulada real em Kestrel, com banco e broker reais e os tempos curtos dos testes (R-13).
/// </summary>
internal static class CenarioPagamentoHttp
{
    public static readonly TimeSpan Prazo = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Sobe o Worker do estoque e do pagamento (sem a entrega) com a API simulada e espera os dois
    /// consumidores ligados, depois de limpar as filas deles.
    /// </summary>
    public static async Task<FabricaWorker> WorkerAsync(
        MensageriaTeste mensageria, string stringConexao, FabricaPagamentos api, CapturaLogs? logs = null,
        IReadOnlyDictionary<string, string?>? configuracoes = null, ColetorTelemetria? telemetria = null, bool limparFilas = true,
        bool comEntrega = false)
    {
        if (limparFilas)
        {
            await mensageria.LimparFilasConsumidorAsync(CenarioEstoque.FilaEstoque);
            await mensageria.LimparFilasConsumidorAsync(CenarioPagamento.FilaPagamento);
            await mensageria.LimparFilasConsumidorAsync(CenarioEntrega.FilaEntrega);
        }

        var todas = new Dictionary<string, string?> { ["Logging:LogLevel:PedidoFulfillment"] = "Debug" };
        foreach (var (chave, valor) in configuracoes ?? new Dictionary<string, string?>())
        {
            todas[chave] = valor;
        }

        var worker = new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
            ApiPagamentos = api,
            Configuracoes = todas,
            Telemetria = telemetria,
            Servicos = servicos =>
            {
                if (!comEntrega)
                {
                    // Sem a entrega, o pedido aprovado fica em Processando (como CenarioPagamento).
                    CenarioEstoque.RemoverConsumidor<CriacaoOrdemEntrega, PagamentoAprovado>(servicos);
                }
                if (logs is not null)
                {
                    servicos.AddSingleton<ILoggerProvider>(logs);
                }
            }
        };
        return await worker.ProntoAsync(async () =>
        {
            (await mensageria.ConsumidorLigadoAsync(CenarioEstoque.FilaEstoque, TimeSpan.FromSeconds(10))).Should().BeTrue("o consumidor de estoque precisa estar pronto");
            (await mensageria.ConsumidorLigadoAsync(CenarioPagamento.FilaPagamento, TimeSpan.FromSeconds(10))).Should().BeTrue("o consumidor de pagamento precisa estar pronto");
            if (comEntrega)
            {
                (await mensageria.ConsumidorLigadoAsync(CenarioEntrega.FilaEntrega, TimeSpan.FromSeconds(10))).Should().BeTrue("o consumidor de entrega precisa estar pronto");
            }
        });
    }

    public static Task<RespostaOperacaoControle?> CobrancaAsync(FabricaPagamentos api, Guid pedidoId) => api.OperacaoAsync("cobranca", pedidoId);

    public static Task<RespostaOperacaoControle?> EstornoAsync(FabricaPagamentos api, Guid pedidoId) => api.OperacaoAsync("estorno", pedidoId);

    /// <summary>SC-002: no máximo uma cobrança e um estorno efetivos por pedido, em todo teste.</summary>
    public static async Task NoMaximoUmaEfetivacaoAsync(FabricaPagamentos api, Guid pedidoId)
    {
        ((await CobrancaAsync(api, pedidoId))?.Efetivacoes ?? 0).Should().BeLessThanOrEqualTo(1, "SC-002: nunca cobrar duas vezes");
        ((await EstornoAsync(api, pedidoId))?.Efetivacoes ?? 0).Should().BeLessThanOrEqualTo(1, "SC-002: nunca estornar duas vezes");
    }

    public static Task<bool> EsperarLogAsync(CapturaLogs logs, int eventId, int quantidade = 1, TimeSpan? prazo = null) =>
        MensageriaTeste.EsperarAteAsync(() => Task.FromResult(logs.Registros.Count(r => r.Evento.Id == eventId) >= quantidade), prazo ?? Prazo);

    public static int Contar(CapturaLogs logs, int eventId) => logs.Registros.Count(r => r.Evento.Id == eventId);
}
