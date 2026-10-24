using System.Collections.Concurrent;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Entregas;
using PedidoFulfillment.Application.Estoque;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Testes.Integracao.Entregas;
using PedidoFulfillment.Testes.Integracao.Estoque;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Pagamentos;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Estornos;

/// <summary>
/// Atalhos dos testes do cancelamento com estorno (REQ-13): pedidos pagos pelo fluxo real, o
/// Worker só com o processador de estornos (rodadas de 1 s) e a leitura do pagamento no banco.
/// </summary>
internal static class CenarioEstorno
{
    public static readonly TimeSpan Prazo = TimeSpan.FromSeconds(15);

    /// <summary>Pedido de 2 do Cat001 e 1 do Cat002, pago pelo Worker de estoque e pagamento, em <c>Processando</c>.</summary>
    public static async Task<Guid> PedidoPagoAsync(MensageriaTeste mensageria, string stringConexao, HttpClient cliente)
    {
        await using var worker = await CenarioPagamento.WorkerProntoAsync(mensageria, stringConexao, taxaAprovacao: 100);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2), (Cat002, 1))).GetProperty("id").GetGuid();
        (await CenarioPagamento.EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue("o pedido precisa estar pago");
        return pedidoId;
    }

    /// <summary>Vários pedidos pagos numa só subida do Worker, na ordem de criação.</summary>
    public static async Task<List<Guid>> PedidosPagosAsync(MensageriaTeste mensageria, string stringConexao, HttpClient cliente, int quantidade)
    {
        await using var worker = await CenarioPagamento.WorkerProntoAsync(mensageria, stringConexao, taxaAprovacao: 100);
        var pedidos = new List<Guid>();
        for (var i = 0; i < quantidade; i++)
        {
            pedidos.Add((await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid());
        }

        foreach (var pedidoId in pedidos)
        {
            (await CenarioPagamento.EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue("o pedido precisa estar pago");
        }

        return pedidos;
    }

    /// <summary>Pedido pago e com ordem de entrega, em <c>ProntoParaEnvio</c>, pelo Worker completo.</summary>
    public static async Task<Guid> PedidoProntoParaEnvioAsync(MensageriaTeste mensageria, string stringConexao, HttpClient cliente)
    {
        await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, stringConexao);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2), (Cat002, 1))).GetProperty("id").GetGuid();
        (await CenarioPagamento.EsperarStatusAsync(stringConexao, pedidoId, "ProntoParaEnvio", Prazo)).Should().BeTrue("o pedido precisa ter a ordem");
        return pedidoId;
    }

    /// <summary>
    /// Worker com o processador de estornos ligado (rodada de 1 s) e os logs de depuração do
    /// sistema. Sem os consumidores: o efeito só pode vir do processador.
    /// </summary>
    public static FabricaWorker WorkerEstornando(
        MensageriaTeste mensageria, string stringConexao, CapturaLogs? logs = null,
        IReadOnlyDictionary<string, string?>? configuracoes = null, Action<IServiceCollection>? servicos = null)
    {
        var todas = new Dictionary<string, string?>
        {
            ["Estornos:Habilitado"] = "true",
            ["Estornos:IntervaloSegundos"] = "1",
            ["Logging:LogLevel:PedidoFulfillment"] = "Debug"
        };
        foreach (var (chave, valor) in configuracoes ?? new Dictionary<string, string?>())
        {
            todas[chave] = valor;
        }

        return new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
            Configuracoes = todas,
            Servicos = colecao =>
            {
                CenarioEstoque.RemoverConsumidor<ReservaEstoque, PedidoCriado>(colecao);
                CenarioEstoque.RemoverConsumidor<ProcessamentoPagamento, EstoqueReservado>(colecao);
                CenarioEstoque.RemoverConsumidor<CriacaoOrdemEntrega, PagamentoAprovado>(colecao);
                if (logs is not null)
                {
                    colecao.AddSingleton<ILoggerProvider>(logs);
                }

                servicos?.Invoke(colecao);
            }
        };
    }

    public static Task<string> SituacaoAsync(string stringConexao, Guid pedidoId) => CenarioPagamento.SituacaoAsync(stringConexao, pedidoId);

    public static Task<int> TentativasAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT TentativasEstorno FROM Pagamentos WHERE PedidoId = '{pedidoId}'");

    public static Task<int> EventosSaidaAsync(string stringConexao) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM EventosSaida");

    /// <summary>Espera a situação do pagamento do pedido, até o prazo.</summary>
    public static Task<bool> EsperarSituacaoAsync(string stringConexao, Guid pedidoId, string situacao, TimeSpan? prazo = null) =>
        MensageriaTeste.EsperarAteAsync(async () => await SituacaoAsync(stringConexao, pedidoId) == situacao, prazo ?? Prazo);

    /// <summary>Cancela pela API como operação e confere o 200.</summary>
    public static async Task CancelarComoOperacaoAsync(HttpClient operador, Guid pedidoId)
    {
        var resposta = await MudarStatusAsync(operador, pedidoId, "Cancelado", "Cliente desistiu depois do pagamento.");
        resposta.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
    }
}

/// <summary>
/// Gateway controlado pelos testes do estorno: a cobrança aprova sempre; o estorno conta as
/// chamadas por pedido e confirma, recusa ou falha conforme <see cref="Modo"/>.
/// </summary>
internal sealed class GatewayEstornoControlado : IGatewayPagamento
{
    public enum ModoEstorno
    {
        Confirmar,
        Falhar,
        Recusar
    }

    private readonly ConcurrentDictionary<Guid, int> estornos = new();
    private readonly ConcurrentDictionary<Guid, bool> semprefalhando = new();

    public volatile ModoEstorno Modo = ModoEstorno.Confirmar;

    public int EstornosDo(Guid pedidoId) => estornos.GetValueOrDefault(pedidoId);

    /// <summary>O estorno deste pedido falha sempre, qualquer que seja o <see cref="Modo"/>.</summary>
    public void FalharSemprePara(Guid pedidoId) => semprefalhando[pedidoId] = true;

    public Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken) =>
        Task.FromResult(ResultadoCobranca.Aprovada($"SIM-{pedidoId:N}"));

    public Task<ResultadoEstorno> EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken cancellationToken)
    {
        estornos.AddOrUpdate(pedidoId, 1, (_, atual) => atual + 1);
        var modo = semprefalhando.ContainsKey(pedidoId) ? ModoEstorno.Falhar : Modo;
        return modo switch
        {
            ModoEstorno.Falhar => throw new TimeoutException("Gateway de pagamento sem resposta."),
            ModoEstorno.Recusar => Task.FromResult(ResultadoEstorno.Recusar("Estorno recusado pelo emissor.")),
            _ => Task.FromResult(ResultadoEstorno.Confirmar($"SIM-EST-{pedidoId:N}"))
        };
    }
}
