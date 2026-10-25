using System.Globalization;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Entregas;
using PedidoFulfillment.Application.Estoque;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Integracao.Estoque;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using RabbitMQ.Client;

namespace PedidoFulfillment.Testes.Integracao.Administracao;

/// <summary>
/// Atalhos dos testes do reenvio da DLQ (REQ-14): mensagens postas direto na DLQ com os
/// cabeçalhos do REQ-06, pedidos gravados no banco e o Worker com o executor ligado.
/// </summary>
internal static class CenarioReenvio
{
    public const string Consumidor = "estoque";
    public static readonly TimeSpan Prazo = TimeSpan.FromSeconds(20);
    private static readonly int[] Intervalos = [100, 200, 400];

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    public static string Dlq => TopologiaMensageria.FilaMortas(Consumidor);

    public static string Principal => TopologiaMensageria.FilaPrincipal(Consumidor);

    /// <summary>Worker com o executor de reenvios (rodada de 1 s), sem consumidores, salvo se pedido.</summary>
    public static FabricaWorker WorkerReenviando(
        MensageriaTeste mensageria, string stringConexao, CapturaLogs? logs = null,
        IReadOnlyDictionary<string, string?>? configuracoes = null, bool comEstoque = false)
    {
        var todas = new Dictionary<string, string?>
        {
            ["ReenvioDlq:Habilitado"] = "true",
            ["ReenvioDlq:IntervaloSegundos"] = "1",
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
                if (!comEstoque)
                {
                    CenarioEstoque.RemoverConsumidor<ReservaEstoque, PedidoCriado>(colecao);
                }

                CenarioEstoque.RemoverConsumidor<ProcessamentoPagamento, EstoqueReservado>(colecao);
                CenarioEstoque.RemoverConsumidor<CriacaoOrdemEntrega, PagamentoAprovado>(colecao);
                if (logs is not null)
                {
                    colecao.AddSingleton<ILoggerProvider>(logs);
                }
            }
        };
    }

    /// <summary>Topologia do consumidor declarada e as filas dele vazias.</summary>
    public static async Task PrepararFilasAsync(MensageriaTeste mensageria)
    {
        await using (var conexao = await mensageria.ConectarAsync())
        await using (var canal = await conexao.CreateChannelAsync(cancellationToken: Cancelamento))
        {
            await TopologiaMensageria.DeclararConsumidorAsync(canal, Consumidor, [TiposEvento.PedidoCriado], Intervalos, Cancelamento);
        }

        await mensageria.LimparFilasConsumidorAsync(Consumidor);
    }

    /// <summary>Uma mensagem na DLQ, com os cabeçalhos que a base de consumo grava (REQ-06).</summary>
    public static async Task<string> NaDlqAsync(
        MensageriaTeste mensageria, string tipo = TiposEvento.PedidoCriado, string motivo = "TimeoutException: sem resposta",
        DateTimeOffset? falhouEm = null, int reenvios = 0, string? corpo = null)
    {
        var id = Guid.CreateVersion7().ToString();
        var cabecalhos = new Dictionary<string, object?>
        {
            ["x-tentativas"] = 3,
            ["x-motivo"] = Encoding.UTF8.GetBytes(motivo),
            ["x-falhou-em"] = Encoding.UTF8.GetBytes((falhouEm ?? DateTimeOffset.UtcNow).ToString("O", CultureInfo.InvariantCulture))
        };
        if (reenvios > 0)
        {
            cabecalhos[CabecalhosReenvio.Reenvios] = reenvios;
        }

        await using var conexao = await mensageria.ConectarAsync();
        await using var canal = await conexao.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), Cancelamento);
        var propriedades = new BasicProperties { Persistent = true, MessageId = id, Type = tipo, ContentType = "application/json", Headers = cabecalhos };
        await canal.BasicPublishAsync("", Dlq, false, propriedades, Encoding.UTF8.GetBytes(corpo ?? $"{{\"teste\":\"{id}\"}}"), Cancelamento);
        return id;
    }

    /// <summary>Lê e consome todas as mensagens da fila, na ordem.</summary>
    public static async Task<List<(string MessageId, IDictionary<string, object?>? Cabecalhos)>> EsvaziarAsync(MensageriaTeste mensageria, string fila)
    {
        var lidas = new List<(string, IDictionary<string, object?>?)>();
        await using var conexao = await mensageria.ConectarAsync();
        await using var canal = await conexao.CreateChannelAsync(cancellationToken: Cancelamento);
        while (await canal.BasicGetAsync(fila, autoAck: true, Cancelamento) is { } mensagem)
        {
            lidas.Add((mensagem.BasicProperties.MessageId!, mensagem.BasicProperties.Headers));
        }

        return lidas;
    }

    public static async Task<uint> ContarAsync(MensageriaTeste mensageria, string fila)
    {
        await using var conexao = await mensageria.ConectarAsync();
        await using var canal = await conexao.CreateChannelAsync(cancellationToken: Cancelamento);
        return (await canal.QueueDeclarePassiveAsync(fila, Cancelamento)).MessageCount;
    }

    public static async Task<PedidoReenvioDlq> PedidoAsync(string stringConexao, FiltroReenvio? filtro = null, DateTimeOffset? registradoEm = null, bool emExecucao = false)
    {
        var pedido = PedidoReenvioDlq.Registrar(Guid.CreateVersion7(), Consumidor, filtro ?? new FiltroReenvio(null, null, null, null), registradoEm ?? DateTimeOffset.UtcNow);
        if (emExecucao)
        {
            pedido.Iniciar(DateTimeOffset.UtcNow);
        }

        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        contexto.PedidosReenvioDlq.Add(pedido);
        await contexto.SaveChangesAsync(Cancelamento);
        return pedido;
    }

    public static async Task<PedidoReenvioDlq> LerAsync(string stringConexao, Guid id)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(
            Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(contexto.PedidosReenvioDlq), p => p.Id == id, Cancelamento);
    }

    public static Task<bool> EsperarSituacaoAsync(string stringConexao, Guid id, SituacaoReenvioDlq situacao) =>
        MensageriaTeste.EsperarAteAsync(async () => (await LerAsync(stringConexao, id)).Situacao == situacao, Prazo);

    public static void DeveTerSido(this PedidoReenvioDlq pedido, int reenviadas, int bloqueadas, int foraDoFiltro) =>
        (pedido.Reenviadas, pedido.BloqueadasPeloLimite, pedido.ForaDoFiltro).Should().Be((reenviadas, bloqueadas, foraDoFiltro));
}
