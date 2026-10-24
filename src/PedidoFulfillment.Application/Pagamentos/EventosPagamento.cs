using Microsoft.Extensions.Logging;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Eventos do pagamento (REQ-09, research R-08). Só identificadores, valores e situação, sem
/// dados pessoais (FR-014).
/// </summary>
public static partial class EventosPagamento
{
    [LoggerMessage(EventId = 7001, EventName = "PagamentoAprovado", Level = LogLevel.Information,
        Message = "Pagamento aprovado para o pedido {PedidoId} (valor {Valor}, transação {IdentificadorTransacao}), evento {IdEvento}.")]
    public static partial void PagamentoAprovado(ILogger logger, Guid idEvento, Guid pedidoId, decimal valor, string identificadorTransacao);

    [LoggerMessage(EventId = 7002, EventName = "PagamentoRecusado", Level = LogLevel.Information,
        Message = "Pagamento recusado para o pedido {PedidoId} (valor {Valor}); estoque devolvido, evento {IdEvento}.")]
    public static partial void PagamentoRecusado(ILogger logger, Guid idEvento, Guid pedidoId, decimal valor);

    [LoggerMessage(EventId = 7003, EventName = "PedidoIgnoradoNoPagamento", Level = LogLevel.Information,
        Message = "Pedido {PedidoId} ignorado no pagamento: status {Status}, estoque reservado: {EstoqueReservado}, já pago: {JaPago}; evento {IdEvento}.")]
    public static partial void PedidoIgnoradoNoPagamento(ILogger logger, Guid idEvento, Guid pedidoId, StatusPedido status, bool estoqueReservado, bool jaPago);

    [LoggerMessage(EventId = 7004, EventName = "ConflitoNoPagamento", Level = LogLevel.Warning,
        Message = "Conflito de concorrência no pagamento do pedido {PedidoId} (reavaliação {Reavaliacao}), evento {IdEvento}.")]
    public static partial void ConflitoNoPagamento(ILogger logger, Guid idEvento, Guid pedidoId, int reavaliacao);

    // Estorno (REQ-13, R-11): sem identificador de transação nem de estorno (FR-016).

    [LoggerMessage(EventId = 7005, EventName = "EstornoSolicitado", Level = LogLevel.Information,
        Message = "Estorno do pedido {PedidoId} (valor {Valor}) solicitado; origem {Origem}.")]
    public static partial void EstornoSolicitado(ILogger logger, Guid pedidoId, decimal valor, OrigemEstorno origem);

    [LoggerMessage(EventId = 7006, EventName = "EstornoConfirmado", Level = LogLevel.Information,
        Message = "Estorno do pedido {PedidoId} (valor {Valor}) confirmado pelo gateway depois de {TentativasAnteriores} tentativa(s) com falha.")]
    public static partial void EstornoConfirmado(ILogger logger, Guid pedidoId, decimal valor, int tentativasAnteriores);

    [LoggerMessage(EventId = 7007, EventName = "FalhaEstorno", Level = LogLevel.Warning,
        Message = "Falha no estorno do pedido {PedidoId} ({Tentativas} tentativa(s) com falha): {Erro}. Será tentado de novo.")]
    public static partial void FalhaEstorno(ILogger logger, Guid pedidoId, int tentativas, string erro);

    [LoggerMessage(EventId = 7008, EventName = "EstornoPendenteAlemDoLimite", Level = LogLevel.Error,
        Message = "Estorno do pedido {PedidoId} pendente há {HorasPendente} h, com {Tentativas} tentativa(s) com falha: {Erro}. Continua sendo tentado; verifique o gateway.")]
    public static partial void EstornoPendenteAlemDoLimite(ILogger logger, Guid pedidoId, int tentativas, int horasPendente, string erro);

    [LoggerMessage(EventId = 7009, EventName = "CobrancaAprovadaParaPedidoCancelado", Level = LogLevel.Warning,
        Message = "Cobrança aprovada para o pedido {PedidoId} (valor {Valor}), cancelado antes da gravação; estorno pendente registrado.")]
    public static partial void CobrancaAprovadaParaPedidoCancelado(ILogger logger, Guid pedidoId, decimal valor);

    [LoggerMessage(EventId = 7010, EventName = "FalhaRodadaEstornos", Level = LogLevel.Warning,
        Message = "Falha na rodada de estornos ({TipoErro}): {Erro}. Tenta de novo no próximo intervalo.")]
    public static partial void FalhaRodadaEstornos(ILogger logger, string tipoErro, string erro);

    [LoggerMessage(EventId = 7011, EventName = "RodadaEstornosIgnorada", Level = LogLevel.Debug,
        Message = "Rodada de estornos ignorada: outra instância está processando.")]
    public static partial void RodadaEstornosIgnorada(ILogger logger);

    [LoggerMessage(EventId = 7012, EventName = "ResumoEstornos",
        Message = "Rodada de estornos: {Confirmados} confirmado(s) e {Falhas} falha(s).")]
    public static partial void ResumoEstornos(ILogger logger, LogLevel level, int confirmados, int falhas);
}
