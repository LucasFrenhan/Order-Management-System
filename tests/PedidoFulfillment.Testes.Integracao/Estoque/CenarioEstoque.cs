using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using RabbitMQ.Client;

namespace PedidoFulfillment.Testes.Integracao.Estoque;

/// <summary>
/// Atalhos dos testes do consumidor de estoque (REQ-08): Worker pronto para consumir, leitura
/// do estado no banco e publicação direta na troca, para reentregas.
/// </summary>
internal static class CenarioEstoque
{
    public const string FilaEstoque = "estoque";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    /// <summary>
    /// Worker só com o consumidor de estoque: sem o pagamento (REQ-09), o pedido reservado
    /// continua <c>Pendente</c>, como estes testes esperam (research R-09 do REQ-09).
    /// </summary>
    public static FabricaWorker Worker(MensageriaTeste mensageria, string stringConexao, CapturaLogs? logs = null) => new()
    {
        StringConexao = stringConexao,
        ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
        Servicos = servicos =>
        {
            var pagamento = servicos.Single(d => d.ImplementationType == typeof(ConsumidorEventos<ProcessamentoPagamento, EstoqueReservado>));
            servicos.Remove(pagamento);
            if (logs is not null)
            {
                servicos.AddSingleton<ILoggerProvider>(logs);
            }
        }
    };

    /// <summary>Sobe o Worker e espera o consumidor de estoque ficar ligado à fila.</summary>
    public static async Task<FabricaWorker> WorkerProntoAsync(MensageriaTeste mensageria, string stringConexao, CapturaLogs? logs = null)
    {
        // Só o pedido.criado deste teste: o dele ainda está no outbox, publicado pelo Worker.
        await mensageria.LimparFilasConsumidorAsync(FilaEstoque);
        var worker = Worker(mensageria, stringConexao, logs);
        worker.CreateClient().Dispose();
        (await mensageria.ConsumidorLigadoAsync(FilaEstoque, TimeSpan.FromSeconds(10))).Should().BeTrue("o consumidor de estoque precisa estar pronto");
        return worker;
    }

    /// <summary>
    /// Declara <c>oms.estoque</c> antes de o Worker subir, com os intervalos do <see cref="FabricaWorker"/>.
    /// Sem isso, num broker novo o despachante pode publicar <c>pedido.criado</c> antes de o
    /// consumidor ligar a fila, e a mensagem sem rota é descartada.
    /// </summary>
    public static async Task DeclararFilaEstoqueAsync(MensageriaTeste mensageria)
    {
        await using var conexao = await mensageria.ConectarAsync();
        await using var canal = await conexao.CreateChannelAsync(cancellationToken: Cancelamento);
        await TopologiaMensageria.DeclararConsumidorAsync(canal, FilaEstoque, [TiposEvento.PedidoCriado], [100, 200, 400], Cancelamento);
    }

    public static Task<int> EstoqueAsync(string stringConexao, Guid produtoId) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT QuantidadeEstoque FROM Produtos WHERE Id = '{produtoId}'");

    public static Task<string> StatusAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT Status FROM Pedidos WHERE Id = '{pedidoId}'");

    public static Task<bool> ReservadoAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<bool>(stringConexao, $"SELECT EstoqueReservado FROM Pedidos WHERE Id = '{pedidoId}'");

    /// <summary>O <c>pedido.criado</c> gravado no outbox para o pedido, como foi publicado.</summary>
    public static Task<string> PedidoCriadoGravadoAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<string>(stringConexao,
            $"SELECT Conteudo FROM EventosSaida WHERE Tipo = '{TiposEvento.PedidoCriado}' AND Conteudo LIKE '%{pedidoId}%'");

    /// <summary>Publica direto na troca, como o despachante faria numa reentrega.</summary>
    public static async Task PublicarAsync(MensageriaTeste mensageria, string corpo, string idEvento, string tipo = TiposEvento.PedidoCriado)
    {
        await using var conexao = await mensageria.ConectarAsync();
        await using var canal = await conexao.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), Cancelamento);
        var propriedades = new BasicProperties { Persistent = true, MessageId = idEvento, Type = tipo };
        await canal.BasicPublishAsync(TopologiaMensageria.TrocaEventos, tipo, false, propriedades, Encoding.UTF8.GetBytes(corpo), Cancelamento);
    }

    /// <summary>
    /// Espera chegar a <c>oms.estoque.dlq</c> uma mensagem que cite o texto (por exemplo o id do
    /// pedido) e devolve os cabeçalhos dela. Lê com <c>BasicGet</c> e confirma o que lê.
    /// </summary>
    public static async Task<IDictionary<string, object?>?> EsperarNaDlqAsync(MensageriaTeste mensageria, string texto, TimeSpan prazo)
    {
        await using var conexao = await mensageria.ConectarAsync();
        await using var canal = await conexao.CreateChannelAsync(cancellationToken: Cancelamento);
        IDictionary<string, object?>? encontrada = null;
        await MensageriaTeste.EsperarAteAsync(async () =>
        {
            while (await canal.BasicGetAsync(TopologiaMensageria.FilaMortas(FilaEstoque), autoAck: true, Cancelamento) is { } mensagem)
            {
                if (Encoding.UTF8.GetString(mensagem.Body.Span).Contains(texto, StringComparison.Ordinal))
                {
                    encontrada = mensagem.BasicProperties.Headers ?? new Dictionary<string, object?>();
                }
            }

            return encontrada is not null;
        }, prazo);
        return encontrada;
    }

    /// <summary>Espera um registro de log com o EventId, por exemplo 5005 (duplicado descartado).</summary>
    public static Task<bool> EsperarLogAsync(CapturaLogs logs, int eventId, TimeSpan prazo) =>
        MensageriaTeste.EsperarAteAsync(() => Task.FromResult(logs.Registros.Any(r => r.Evento.Id == eventId)), prazo);

    /// <summary>Mensagens de <c>estoque.*</c> recebidas para o pedido.</summary>
    public static List<MensagemRecebida> DoPedido(FilaTeste fila, Guid pedidoId) =>
        [.. fila.Mensagens.Where(m => m.Texto.Contains(pedidoId.ToString(), StringComparison.Ordinal))];
}
