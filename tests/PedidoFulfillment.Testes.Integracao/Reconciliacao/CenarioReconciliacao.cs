using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Entregas;
using PedidoFulfillment.Application.Estoque;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Application.Reconciliacao;
using PedidoFulfillment.Testes.Integracao.Entregas;
using PedidoFulfillment.Testes.Integracao.Estoque;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Pagamentos;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Reconciliacao;

/// <summary>
/// Atalhos dos testes da reconciliação (REQ-12): pedidos parados em cada etapa, com o evento
/// "perdido" (marcado como enviado sem ter chegado a ninguém) e a data atrasada, e o Worker com a
/// reconciliação ligada e ciclos de 1 s.
/// </summary>
internal static class CenarioReconciliacao
{
    public static readonly TimeSpan Prazo = TimeSpan.FromSeconds(20);

    private static readonly string[] Consumidores = [CenarioEstoque.FilaEstoque, CenarioPagamento.FilaPagamento, CenarioEntrega.FilaEntrega];

    /// <summary>
    /// Worker com a reconciliação ligada (ciclo de 1 s, parado há mais de 60 s) e os logs de
    /// depuração do sistema. Sem os consumidores, por padrão: o efeito só pode vir da reconciliação.
    /// </summary>
    public static FabricaWorker WorkerReconciliando(
        MensageriaTeste mensageria, string stringConexao, CapturaLogs? logs = null,
        IReadOnlyDictionary<string, string?>? configuracoes = null, Action<IServiceCollection>? servicos = null, bool comConsumidores = false)
    {
        var todas = new Dictionary<string, string?>
        {
            ["Reconciliacao:Habilitada"] = "true",
            ["Reconciliacao:IntervaloCicloSegundos"] = "1",
            ["Reconciliacao:LimiteParadoSegundos"] = "60",
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
                if (!comConsumidores)
                {
                    CenarioEstoque.RemoverConsumidor<ReservaEstoque, PedidoCriado>(colecao);
                    CenarioEstoque.RemoverConsumidor<ProcessamentoPagamento, EstoqueReservado>(colecao);
                    CenarioEstoque.RemoverConsumidor<CriacaoOrdemEntrega, PagamentoAprovado>(colecao);
                }

                if (logs is not null)
                {
                    colecao.AddSingleton<ILoggerProvider>(logs);
                }

                servicos?.Invoke(colecao);
            }
        };
    }

    /// <summary>Pedido <c>Pendente</c> sem reserva, com o <c>pedido.criado</c> perdido.</summary>
    public static async Task<Guid> ParadoNoEstoqueAsync(MensageriaTeste mensageria, string stringConexao, HttpClient cliente)
    {
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2), (Cat002, 1))).GetProperty("id").GetGuid();
        await PerderEventosAsync(mensageria, stringConexao);
        return pedidoId;
    }

    /// <summary>Pedido reservado sem cobrança, com o <c>estoque.reservado</c> perdido.</summary>
    public static async Task<Guid> ParadoNoPagamentoAsync(MensageriaTeste mensageria, string stringConexao, HttpClient cliente)
    {
        await using (var worker = await CenarioEstoque.WorkerProntoAsync(mensageria, stringConexao))
        {
            var pedidoId = (await CriadoAsync(cliente, (Cat001, 2), (Cat002, 1))).GetProperty("id").GetGuid();
            (await MensageriaTeste.EsperarAteAsync(() => CenarioEstoque.ReservadoAsync(stringConexao, pedidoId), Prazo)).Should().BeTrue();
            await PerderEventosAsync(mensageria, stringConexao);
            return pedidoId;
        }
    }

    /// <summary>Pedido pago em <c>Processando</c> sem ordem, com o <c>pagamento.aprovado</c> perdido (caso do REQ-10).</summary>
    public static async Task<Guid> ParadoNaEntregaAsync(MensageriaTeste mensageria, string stringConexao, HttpClient cliente) =>
        (await ParadosNaEntregaAsync(mensageria, stringConexao, cliente, 1))[0];

    /// <summary>Vários pedidos pagos sem ordem, com um Worker de estoque e pagamento só.</summary>
    public static async Task<IReadOnlyList<Guid>> ParadosNaEntregaAsync(MensageriaTeste mensageria, string stringConexao, HttpClient cliente, int quantidade)
    {
        await using (var worker = await CenarioPagamento.WorkerProntoAsync(mensageria, stringConexao, 100))
        {
            var ids = new List<Guid>();
            for (var i = 0; i < quantidade; i++)
            {
                ids.Add((await CriadoAsync(cliente, (Cat001, 2), (Cat002, 1))).GetProperty("id").GetGuid());
            }

            foreach (var pedidoId in ids)
            {
                (await CenarioPagamento.EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
            }

            await PerderEventosAsync(mensageria, stringConexao);
            return ids;
        }
    }

    /// <summary>
    /// Os pendentes são dados como enviados e as filas esvaziadas: o evento se perdeu, como num
    /// broker sem fila antes do REQ-12.
    /// </summary>
    public static async Task PerderEventosAsync(MensageriaTeste mensageria, string stringConexao)
    {
        await BancoDadosTeste.ExecutarAsync(stringConexao, "UPDATE EventosSaida SET EnviadoEm = SYSDATETIMEOFFSET() WHERE EnviadoEm IS NULL");
        await LimparFilasAsync(mensageria);
    }

    public static async Task LimparFilasAsync(MensageriaTeste mensageria)
    {
        foreach (var consumidor in Consumidores)
        {
            await mensageria.LimparFilasConsumidorAsync(consumidor);
        }
    }

    /// <summary>Atrasa a última atualização do pedido.</summary>
    public static Task EnvelhecerAsync(string stringConexao, Guid pedidoId, TimeSpan idade) =>
        BancoDadosTeste.ExecutarAsync(stringConexao,
            $"UPDATE Pedidos SET AtualizadoEm = DATEADD(second, -{idade.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)}, SYSDATETIMEOFFSET()) WHERE Id = '{pedidoId}'");

    /// <summary>Eventos do tipo gravados no outbox para o pedido.</summary>
    public static Task<int> EventosAsync(string stringConexao, Guid pedidoId, string tipo) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM EventosSaida WHERE Tipo = '{tipo}' AND Conteudo LIKE '%{pedidoId}%'");

    public static Task<string> ConteudoAsync(string stringConexao, Guid pedidoId, string tipo) =>
        BancoDadosTeste.ConsultarAsync<string>(stringConexao,
            $"SELECT Conteudo FROM EventosSaida WHERE Tipo = '{tipo}' AND Conteudo LIKE '%{pedidoId}%'");

    /// <summary>Uma regra de etapa que sempre falha, registrada depois da original (o último registro vence).</summary>
    public static Action<IServiceCollection> RegraQueFalha(EtapaFluxoPedido etapa) =>
        servicos => servicos.AddScoped<IEtapaFluxoPedido>(_ => new RegraQueFalhaSempre(etapa));

    /// <summary>Espera <paramref name="quantidade"/> registros com o EventId, até o prazo.</summary>
    public static Task<bool> EsperarLogsAsync(CapturaLogs logs, int eventId, int quantidade = 1) =>
        MensageriaTeste.EsperarAteAsync(() => Task.FromResult(logs.Registros.Count(r => r.Evento.Id == eventId) >= quantidade), Prazo);

    private sealed class RegraQueFalhaSempre(EtapaFluxoPedido etapa) : IEtapaFluxoPedido
    {
        public EtapaFluxoPedido Etapa => etapa;

        public Task<bool> AplicarAsync(Guid idOrigem, Guid pedidoId, CancellationToken cancellationToken) =>
            throw new TimeoutException($"Regra de {etapa} sem resposta.");
    }
}
