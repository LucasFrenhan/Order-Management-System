using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Guarda tudo o que a API registra em log (mensagem, propriedades e exceção), para provar que
/// nada sensível chega aos logs (SC-003).
/// </summary>
public sealed class CapturaLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<(string Categoria, EventId Evento, string Texto)> registros = new();

    public IReadOnlyCollection<(string Categoria, EventId Evento, string Texto)> Registros => registros;

    public IEnumerable<string> Textos => registros.Select(r => r.Texto);

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, registros);

    public void Dispose()
    {
    }

    private sealed class Logger(string categoria, ConcurrentQueue<(string, EventId, string)> registros) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var propriedades = state is IEnumerable<KeyValuePair<string, object?>> pares
                ? string.Join(" ", pares.Select(p => $"{p.Key}={p.Value}"))
                : "";
            registros.Enqueue((categoria, eventId, $"{formatter(state, exception)} {propriedades} {exception}"));
        }
    }
}
