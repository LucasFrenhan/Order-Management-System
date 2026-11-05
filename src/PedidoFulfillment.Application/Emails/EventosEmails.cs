using Microsoft.Extensions.Logging;

namespace PedidoFulfillment.Application.Emails;

/// <summary>
/// Logs do envio de e-mail (REQ-24, R-10). Nunca recebem o token, o corpo nem o e-mail completo.
/// Públicos porque quem envia é o processador da Infrastructure.
/// </summary>
public static partial class EventosEmails
{
    [LoggerMessage(EventId = 3016, EventName = "EmailEnviado", Level = LogLevel.Information,
        Message = "E-mail {Tipo} enviado ao usuário {UsuarioId} ({EmailMascarado}).")]
    public static partial void EmailEnviado(ILogger logger, TipoEmail tipo, Guid usuarioId, string emailMascarado);

    [LoggerMessage(EventId = 3017, EventName = "FalhaEnvioEmail", Level = LogLevel.Warning,
        Message = "Falha no envio de e-mail ({Tipo}), tentativa {Tentativas}, {EmailMascarado}: {Motivo}. Tenta de novo mais tarde.")]
    public static partial void FalhaEnvioEmail(ILogger logger, string tipo, int tentativas, string emailMascarado, string motivo);

    [LoggerMessage(EventId = 3018, EventName = "EmailDescartado", Level = LogLevel.Warning,
        Message = "E-mail {Tipo} descartado ({EmailMascarado}): {Motivo}.")]
    public static partial void EmailDescartado(ILogger logger, TipoEmail tipo, string emailMascarado, string motivo);
}
