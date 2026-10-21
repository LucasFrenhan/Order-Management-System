using Microsoft.Extensions.Logging;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>
/// Eventos de segurança (research R-10). Nunca recebem senha, token nem o e-mail completo.
/// </summary>
internal static partial class EventosAutenticacao
{
    [LoggerMessage(EventId = 3001, EventName = "UsuarioRegistrado", Level = LogLevel.Information,
        Message = "Usuário {UsuarioId} registrado ({EmailMascarado}).")]
    public static partial void UsuarioRegistrado(ILogger logger, Guid usuarioId, string emailMascarado);

    [LoggerMessage(EventId = 3002, EventName = "LoginRealizado", Level = LogLevel.Information,
        Message = "Login realizado pelo usuário {UsuarioId}.")]
    public static partial void LoginRealizado(ILogger logger, Guid usuarioId);

    /// <summary>O motivo real fica só no log; a resposta é sempre o mesmo 401 genérico.</summary>
    [LoggerMessage(EventId = 3003, EventName = "LoginRecusado", Level = LogLevel.Warning,
        Message = "Login recusado para {EmailMascarado} (usuário {UsuarioId}): {Motivo}.")]
    public static partial void LoginRecusado(ILogger logger, string emailMascarado, Guid? usuarioId, string motivo);

    [LoggerMessage(EventId = 3004, EventName = "ContaBloqueada", Level = LogLevel.Warning,
        Message = "Conta do usuário {UsuarioId} ({EmailMascarado}) bloqueada após tentativas de login erradas.")]
    public static partial void ContaBloqueada(ILogger logger, Guid usuarioId, string emailMascarado);
}
