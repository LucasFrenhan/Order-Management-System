using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Testes.Unitarios.Autenticacao;

/// <summary>
/// Gestor de identidade falso: devolve o resultado configurado e guarda os argumentos recebidos.
/// </summary>
public sealed class GestorIdentidadeFalso : IGestorIdentidade
{
    public Resultado<UsuarioResponseDTO> ResultadoCriacao { get; set; } = new UsuarioResponseDTO
    {
        Id = Guid.NewGuid(),
        Nome = "Maria Silva",
        Email = "Maria@Exemplo.com.br",
        Perfis = [PerfilUsuario.Cliente]
    };

    public ResultadoCredenciais ResultadoVerificacao { get; set; } = ResultadoCredenciais.Invalida(null);

    public (string Nome, string Email, string Senha)? UltimaCriacao { get; private set; }

    public (string Email, string Senha)? UltimaVerificacao { get; private set; }

    public Task<Resultado<UsuarioResponseDTO>> CriarAsync(string nome, string email, string senha, CancellationToken cancellationToken)
    {
        UltimaCriacao = (nome, email, senha);
        return Task.FromResult(ResultadoCriacao);
    }

    public Task<ResultadoCredenciais> VerificarCredenciaisAsync(string email, string senha, CancellationToken cancellationToken)
    {
        UltimaVerificacao = (email, senha);
        return Task.FromResult(ResultadoVerificacao);
    }
}

public sealed class EmissorTokenFalso : IEmissorToken
{
    public LoginResponseDTO Emitir(Guid usuarioId, IReadOnlyList<PerfilUsuario> perfis) => new()
    {
        TokenAcesso = $"token-de-{usuarioId}",
        ExpiraEm = new DateTimeOffset(2026, 10, 21, 13, 0, 0, TimeSpan.Zero),
        Perfis = perfis
    };
}

/// <summary>Um evento registrado: id, nível, texto formatado e propriedades estruturadas.</summary>
public sealed record EventoLog(EventId Id, LogLevel Nivel, string Mensagem, IReadOnlyDictionary<string, object?> Propriedades);

public sealed class LoggerFalso<T> : ILogger<T>
{
    public List<EventoLog> Eventos { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var propriedades = (state as IEnumerable<KeyValuePair<string, object?>> ?? [])
            .ToDictionary(p => p.Key, p => p.Value);
        Eventos.Add(new EventoLog(eventId, logLevel, formatter(state, exception), propriedades));
    }
}
