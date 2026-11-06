using Microsoft.Extensions.Logging;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Usuarios;

/// <summary>
/// Auditoria da gestão de usuários (REQ-25, FR-021): só a ação, os ids e o perfil, nunca nome, e-mail,
/// endereço de origem ou credencial. Registrada depois de a transação terminar.
/// </summary>
internal static partial class EventosGestaoUsuarios
{
    [LoggerMessage(EventId = 3020, EventName = "UsuarioInternoCriado", Level = LogLevel.Information,
        Message = "Usuário {AlvoId} criado por {AtorId}, com o perfil {Perfil}.")]
    public static partial void UsuarioInternoCriado(ILogger logger, Guid alvoId, Guid atorId, PerfilUsuario perfil);

    [LoggerMessage(EventId = 3021, EventName = "PerfilAtribuido", Level = LogLevel.Information,
        Message = "Perfil {Perfil} atribuído ao usuário {AlvoId} por {AtorId}.")]
    public static partial void PerfilAtribuido(ILogger logger, PerfilUsuario perfil, Guid alvoId, Guid atorId);

    [LoggerMessage(EventId = 3022, EventName = "PerfilRetirado", Level = LogLevel.Information,
        Message = "Perfil {Perfil} retirado do usuário {AlvoId} por {AtorId}.")]
    public static partial void PerfilRetirado(ILogger logger, PerfilUsuario perfil, Guid alvoId, Guid atorId);

    [LoggerMessage(EventId = 3023, EventName = "UsuarioDesativado", Level = LogLevel.Information,
        Message = "Usuário {AlvoId} desativado por {AtorId}.")]
    public static partial void UsuarioDesativado(ILogger logger, Guid alvoId, Guid atorId);

    [LoggerMessage(EventId = 3024, EventName = "UsuarioReativado", Level = LogLevel.Information,
        Message = "Usuário {AlvoId} reativado por {AtorId}.")]
    public static partial void UsuarioReativado(ILogger logger, Guid alvoId, Guid atorId);

    [LoggerMessage(EventId = 3025, EventName = "AlteracaoUsuarioRecusada", Level = LogLevel.Warning,
        Message = "Alteração {Alteracao} (perfil {Perfil}) do usuário {AlvoId} pedida por {AtorId} recusada: {Codigo}.")]
    public static partial void AlteracaoUsuarioRecusada(ILogger logger, TipoAlteracaoUsuario alteracao, PerfilUsuario? perfil, Guid alvoId, Guid atorId, string? codigo);
}
