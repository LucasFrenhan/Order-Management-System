using Microsoft.Extensions.Logging;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Eventos da limpeza, do reenvio e do aviso da DLQ (REQ-14, R-11). Só identificadores,
/// contagens e filtros; o autor é o identificador do usuário, nunca o e-mail.
/// </summary>
internal static partial class EventosLimpezaReenvio
{
    [LoggerMessage(EventId = 5009, EventName = "ReenvioDlqConcluido", Level = LogLevel.Information,
        Message = "Pedido de reenvio {PedidoReenvioId} de {AutorId} concluído na DLQ de {Consumidor} (filtro {Filtro}): {Reenviadas} reenviada(s), {Bloqueadas} bloqueada(s) pelo limite e {ForaDoFiltro} fora do filtro.")]
    public static partial void ReenvioConcluido(
        ILogger logger, Guid pedidoReenvioId, Guid autorId, string consumidor, string filtro, int reenviadas, int bloqueadas, int foraDoFiltro);

    [LoggerMessage(EventId = 5010, EventName = "ReenvioDlqFalhou", Level = LogLevel.Warning,
        Message = "Pedido de reenvio {PedidoReenvioId} na DLQ de {Consumidor} não terminou ({Reenviadas} reenviada(s) até a falha; situação {Situacao}): {Erro}")]
    public static partial void ReenvioFalhou(ILogger logger, Guid pedidoReenvioId, string consumidor, int reenviadas, string situacao, string erro);

    [LoggerMessage(EventId = 5011, EventName = "DlqAcimaDoLimite", Level = LogLevel.Warning,
        Message = "A DLQ de {Consumidor} tem {Quantidade} mensagem(ns), acima do limite de {Limite}. Investigue a causa e, se for o caso, peça o reenvio.")]
    public static partial void DlqAcimaDoLimite(ILogger logger, string consumidor, uint quantidade, int limite);

    [LoggerMessage(EventId = 5012, EventName = "FalhaVerificacaoDlq", Level = LogLevel.Warning,
        Message = "Falha ao conferir o tamanho das DLQs ({TipoErro}): {Erro}. Tenta de novo no próximo intervalo.")]
    public static partial void FalhaVerificacaoDlq(ILogger logger, string tipoErro, string erro);

    [LoggerMessage(EventId = 5013, EventName = "LimpezaConcluida",
        Message = "Limpeza: {Enviados} evento(s) enviado(s), {Processados} registro(s) de processado, {Chaves} chave(s) de idempotência e {PedidosReenvio} pedido(s) de reenvio apagados.")]
    public static partial void LimpezaConcluida(ILogger logger, LogLevel level, int enviados, int processados, int chaves, int pedidosReenvio);

    [LoggerMessage(EventId = 5014, EventName = "FalhaLimpeza", Level = LogLevel.Warning,
        Message = "Falha na rodada de limpeza ({TipoErro}): {Erro}. Tenta de novo no próximo intervalo.")]
    public static partial void FalhaLimpeza(ILogger logger, string tipoErro, string erro);

    [LoggerMessage(EventId = 5015, EventName = "RodadaIgnorada", Level = LogLevel.Debug,
        Message = "Rodada de {Rotina} ignorada: outra instância está executando.")]
    public static partial void RodadaIgnorada(ILogger logger, string rotina);
}
