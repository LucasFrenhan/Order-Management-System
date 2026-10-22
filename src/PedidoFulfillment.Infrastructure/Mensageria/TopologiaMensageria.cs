using RabbitMQ.Client;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Trocas e filas (REQ-06, R-06; <c>contracts/topologia-e-consumo.md</c>), todas duráveis e
/// declaradas de forma idempotente. Por consumidor C: <c>oms.C</c>, uma fila de espera por
/// intervalo de nova tentativa, que devolve a mensagem para <c>oms.C</c> ao expirar, e a DLQ.
/// </summary>
internal static class TopologiaMensageria
{
    public const string TrocaEventos = "oms.eventos";

    public static string FilaPrincipal(string consumidor) => $"oms.{consumidor}";

    public static string FilaEspera(string consumidor, int intervaloMs) => $"oms.{consumidor}.espera.{intervaloMs}";

    public static string FilaMortas(string consumidor) => $"oms.{consumidor}.dlq";

    public static Task DeclararTrocaAsync(IChannel canal, CancellationToken cancellationToken) =>
        canal.ExchangeDeclareAsync(TrocaEventos, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);

    public static async Task DeclararConsumidorAsync(
        IChannel canal, string consumidor, IEnumerable<string> tipos, IReadOnlyList<int> intervalosMs, CancellationToken cancellationToken)
    {
        await DeclararTrocaAsync(canal, cancellationToken);
        var principal = FilaPrincipal(consumidor);
        await canal.QueueDeclareAsync(principal, durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        foreach (var tipo in tipos)
        {
            await canal.QueueBindAsync(principal, TrocaEventos, tipo, cancellationToken: cancellationToken);
        }

        foreach (var intervalo in intervalosMs)
        {
            // Ao expirar, a mensagem volta pela troca padrão para a fila principal (FR-012).
            var argumentos = new Dictionary<string, object?>
            {
                ["x-message-ttl"] = intervalo,
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = principal
            };
            await canal.QueueDeclareAsync(FilaEspera(consumidor, intervalo), durable: true, exclusive: false, autoDelete: false, argumentos, cancellationToken: cancellationToken);
        }

        await canal.QueueDeclareAsync(FilaMortas(consumidor), durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
    }
}
