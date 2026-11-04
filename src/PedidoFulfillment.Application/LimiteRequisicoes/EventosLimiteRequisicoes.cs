using Microsoft.Extensions.Logging;

namespace PedidoFulfillment.Application.LimiteRequisicoes;

/// <summary>Logs do limite (R-08). Nunca recebem senha, e-mail completo nem o endereço de origem.</summary>
internal static partial class EventosLimiteRequisicoes
{
    /// <summary>Um por contagem em cada janela (FR-020), para uma inundação não inundar também o log.</summary>
    [LoggerMessage(EventId = 3007, EventName = "LimiteRequisicoesExcedido", Level = LogLevel.Warning,
        Message = "Limite de requisições excedido na política {Politica} ({EmailMascarado}).")]
    public static partial void LimiteExcedido(ILogger logger, string politica, string emailMascarado);

    /// <summary>Sem a mensagem da exceção: ela pode citar o servidor do banco.</summary>
    [LoggerMessage(EventId = 3008, EventName = "FalhaArmazenamentoLimite", Level = LogLevel.Warning,
        Message = "Armazenamento dos limites indisponível ({TipoFalha}): as requisições seguem sem o limite.")]
    public static partial void FalhaArmazenamento(ILogger logger, string tipoFalha);
}
