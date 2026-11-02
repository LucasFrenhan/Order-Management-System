using Microsoft.Extensions.Logging;

namespace PedidoFulfillment.PagamentosSimulados;

/// <summary>
/// Logs da API simulada (REQ-19, research R-12; contrato › Logs da API simulada), no console, com o
/// <c>traceparent</c> recebido no escopo. Nunca a chave de acesso, a de controle ou o corpo.
/// </summary>
internal static partial class Logs
{
    [LoggerMessage(EventId = 11001, EventName = "OperacaoProcessada", Level = LogLevel.Information,
        Message = "{Tipo} {Chave}: {Situacao} (comportamento {Comportamento}, latência {LatenciaMs} ms).")]
    public static partial void OperacaoProcessada(ILogger logger, string tipo, Guid chave, string situacao, string comportamento, int latenciaMs);

    [LoggerMessage(EventId = 11002, EventName = "FalhaSimulada", Level = LogLevel.Information,
        Message = "{Tipo} {Chave}: falha simulada {Comportamento}.")]
    public static partial void FalhaSimulada(ILogger logger, string tipo, Guid chave, string comportamento);

    [LoggerMessage(EventId = 11003, EventName = "RepeticaoIdempotente", Level = LogLevel.Information,
        Message = "{Tipo} {Chave}: repetição da chave; devolvido o resultado guardado ({Situacao}), chamada {Chamadas}.")]
    public static partial void RepeticaoIdempotente(ILogger logger, string tipo, Guid chave, string situacao, int chamadas);

    [LoggerMessage(EventId = 11004, EventName = "ChaveReutilizada", Level = LogLevel.Warning,
        Message = "{Tipo} {Chave}: chave reutilizada com outro corpo; recusada.")]
    public static partial void ChaveReutilizada(ILogger logger, string tipo, Guid chave);

    [LoggerMessage(EventId = 11005, EventName = "ComportamentoAlterado", Level = LogLevel.Information,
        Message = "Comportamento da simulação alterado: {Alteracao}.")]
    public static partial void ComportamentoAlterado(ILogger logger, string alteracao);

    [LoggerMessage(EventId = 11006, EventName = "CredencialRecusada", Level = LogLevel.Warning,
        Message = "Credencial ausente ou inválida em {Metodo} {Rota}.")]
    public static partial void CredencialRecusada(ILogger logger, string metodo, string rota);
}
