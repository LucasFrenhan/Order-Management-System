namespace PedidoFulfillment.API.Seguranca;

/// <summary>
/// Eventos das chaves do JWT (REQ-27, research R-05; contracts/configuracao.md). Citam só identificadores, que
/// são públicos; nunca o valor de uma chave, o token, o usuário ou o endereço de origem.
/// </summary>
internal static partial class EventosChavesJwt
{
    [LoggerMessage(EventId = 3026, EventName = "ChavesJwtCarregadas", Level = LogLevel.Information,
        Message = "Chaves do JWT: ativa {ChaveAtiva}; aceitas {ChavesAceitas}.")]
    public static partial void ChavesJwtCarregadas(ILogger logger, string chaveAtiva, string chavesAceitas);

    /// <summary>
    /// Token recusado porque o <c>kid</c> falta ou não está configurado (FR-016). O identificador vem de quem chama:
    /// só entra no log se tiver o formato de um identificador; senão, <c>(inválido)</c> ou <c>(ausente)</c>.
    /// </summary>
    [LoggerMessage(EventId = 3027, EventName = "TokenChaveDesconhecida", Level = LogLevel.Warning,
        Message = "Token recusado: chave {IdentificadorChave} fora da configuração.")]
    public static partial void TokenChaveDesconhecida(ILogger logger, string identificadorChave);
}
