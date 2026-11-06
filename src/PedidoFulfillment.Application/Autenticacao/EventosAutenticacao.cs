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

    [LoggerMessage(EventId = 3010, EventName = "RenovacaoReutilizada", Level = LogLevel.Warning,
        Message = "Token de renovação reutilizado: a sessão do usuário {UsuarioId} foi encerrada.")]
    public static partial void RenovacaoReutilizada(ILogger logger, Guid usuarioId);

    [LoggerMessage(EventId = 3011, EventName = "SessaoEncerrada", Level = LogLevel.Information,
        Message = "Sessão do usuário {UsuarioId} encerrada por logout.")]
    public static partial void SessaoEncerrada(ILogger logger, Guid usuarioId);

    [LoggerMessage(EventId = 3012, EventName = "SenhaTrocada", Level = LogLevel.Information,
        Message = "Senha do usuário {UsuarioId} trocada; todas as sessões foram encerradas.")]
    public static partial void SenhaTrocada(ILogger logger, Guid usuarioId);

    [LoggerMessage(EventId = 3013, EventName = "TrocaSenhaRecusada", Level = LogLevel.Warning,
        Message = "Troca de senha recusada para o usuário {UsuarioId}: senha atual incorreta ou conta bloqueada.")]
    public static partial void TrocaSenhaRecusada(ILogger logger, Guid usuarioId);

    [LoggerMessage(EventId = 3014, EventName = "SenhaRedefinida", Level = LogLevel.Information,
        Message = "Senha do usuário {UsuarioId} redefinida; todas as sessões foram encerradas.")]
    public static partial void SenhaRedefinida(ILogger logger, Guid usuarioId);

    [LoggerMessage(EventId = 3015, EventName = "EmailConfirmado", Level = LogLevel.Information,
        Message = "E-mail do usuário {UsuarioId} confirmado.")]
    public static partial void EmailConfirmado(ILogger logger, Guid usuarioId);

    [LoggerMessage(EventId = 3019, EventName = "ConviteAceito", Level = LogLevel.Information,
        Message = "Convite aceito: o usuário {UsuarioId} definiu a senha.")]
    public static partial void ConviteAceito(ILogger logger, Guid usuarioId);
}
