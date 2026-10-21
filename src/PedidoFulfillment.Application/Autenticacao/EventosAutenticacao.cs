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
}
