using Microsoft.Extensions.Logging;
using PedidoFulfillment.Testes.Integracao.Autenticacao;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Guarda a exceção original quando o host não sobe. Quando a partida falha, o <c>Main</c> da
/// aplicação libera o host, e o <c>WebApplicationFactory</c> pode consultar os serviços depois
/// disso: a exceção que chega ao teste vira <see cref="ObjectDisposedException"/>, de forma
/// intermitente (CI do PR #22). O host registra "Hosting failed to start"
/// (<c>HostedServiceStartupFaulted</c>) com a exceção original antes de ser liberado.
/// </summary>
public sealed class FalhaPartida : ILoggerProvider
{
    private const string EventoFalhaPartida = "HostedServiceStartupFaulted";

    private Exception? original;

    /// <summary>A exceção registrada pelo host, se houve falha de partida.</summary>
    public Exception? Original => Volatile.Read(ref original);

    /// <summary>
    /// A cadeia da exceção original. A exceção lançada ao teste só confirma que a partida falhou;
    /// sem o registro do host, o teste falha, em vez de avaliar a exceção errada.
    /// </summary>
    public IEnumerable<Exception> Cadeia(Exception lancada)
    {
        ArgumentNullException.ThrowIfNull(lancada);
        return PreparacaoInicializacaoTestes.Cadeia(
            Original ?? throw new InvalidOperationException($"O host não registrou {EventoFalhaPartida}; a exceção lançada foi {lancada.GetType().Name}."));
    }

    public ILogger CreateLogger(string categoryName) => new Registrador(this);

    public void Dispose()
    {
    }

    private sealed class Registrador(FalhaPartida falha) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Name == EventoFalhaPartida && exception is not null)
            {
                Interlocked.CompareExchange(ref falha.original, exception, null);
            }
        }
    }
}
