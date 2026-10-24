using Microsoft.Extensions.Logging;

namespace PedidoFulfillment.Application.Reconciliacao;

/// <summary>
/// Eventos da reconciliação (REQ-12, R-11). Só identificadores, etapas e contagens, sem dados
/// pessoais (FR-015). Públicos porque o ciclo, no Infrastructure, também os escreve.
/// </summary>
public static partial class EventosReconciliacao
{
    [LoggerMessage(EventId = 9001, EventName = "PedidoRecuperado", Level = LogLevel.Warning,
        Message = "Pedido {PedidoId} parado na etapa {Etapa} recuperado pela reconciliação.")]
    public static partial void PedidoRecuperado(ILogger logger, Guid pedidoId, EtapaFluxoPedido etapa);

    [LoggerMessage(EventId = 9002, EventName = "PedidoEsgotado", Level = LogLevel.Error,
        Message = "Pedido {PedidoId} esgotado na etapa {Etapa} depois de {Falhas} ciclo(s) com falha e cancelado: {Motivo}")]
    public static partial void PedidoEsgotado(ILogger logger, Guid pedidoId, EtapaFluxoPedido etapa, int falhas, string motivo);

    [LoggerMessage(EventId = 9003, EventName = "FalhaPedidoReconciliacao", Level = LogLevel.Warning,
        Message = "Falha ao reconciliar o pedido {PedidoId} na etapa {Etapa} ({Falhas} ciclo(s) com falha): {Erro}")]
    public static partial void FalhaPedido(ILogger logger, Guid pedidoId, EtapaFluxoPedido etapa, int falhas, string erro);

    /// <summary>Warning se o ciclo recuperou ou esgotou algum pedido; Debug se não (US3, cenário 2).</summary>
    [LoggerMessage(EventId = 9004, EventName = "ResumoReconciliacao",
        Message = "Reconciliação: recuperados {Estoque} no estoque, {Pagamento} no pagamento e {Entrega} na entrega; {Esgotados} esgotado(s) e {Falhas} falha(s).")]
    public static partial void Resumo(ILogger logger, LogLevel nivel, int estoque, int pagamento, int entrega, int esgotados, int falhas);

    [LoggerMessage(EventId = 9005, EventName = "FalhaCicloReconciliacao", Level = LogLevel.Warning,
        Message = "Falha no ciclo da reconciliação ({Erro}): {Mensagem}")]
    public static partial void FalhaCiclo(ILogger logger, string erro, string mensagem);

    [LoggerMessage(EventId = 9006, EventName = "CicloReconciliacaoIgnorado", Level = LogLevel.Debug,
        Message = "Ciclo da reconciliação ignorado: outra instância está reconciliando.")]
    public static partial void CicloIgnorado(ILogger logger);

    [LoggerMessage(EventId = 9007, EventName = "PedidoNaoMaisParado", Level = LogLevel.Debug,
        Message = "Pedido {PedidoId} não está mais parado na etapa {Etapa}.")]
    public static partial void PedidoNaoMaisParado(ILogger logger, Guid pedidoId, EtapaFluxoPedido etapa);
}
