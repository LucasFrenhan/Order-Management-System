using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Pagamentos;

namespace PedidoFulfillment.Infrastructure.Pagamentos;

/// <summary>
/// Logs do cliente do gateway de pagamento (REQ-19, FR-021, research R-12; contrato do cliente ›
/// Logs). Sem chave de acesso, identificador de transação, corpo ou dados pessoais. O 7019
/// (<c>ConsultaCobranca</c>) fica no Application, onde a consulta é decidida.
/// </summary>
internal static partial class EventosGatewayPagamento
{
    [LoggerMessage(EventId = 7013, EventName = "FalhaPassageiraGateway", Level = LogLevel.Warning,
        Message = "Falha passageira do gateway na {Operacao} do pedido {PedidoId}: {TipoFalha} na tentativa {Tentativa}. Tentando de novo com a mesma chave.")]
    public static partial void FalhaPassageira(ILogger logger, OperacaoGateway operacao, Guid pedidoId, string tipoFalha, int tentativa);

    [LoggerMessage(EventId = 7014, EventName = "ResultadoIncertoGateway", Level = LogLevel.Warning,
        Message = "Resultado incerto na {Operacao} do pedido {PedidoId}: tempo esgotado na tentativa {Tentativa}. Nada gravado; tentando de novo com a mesma chave.")]
    public static partial void ResultadoIncerto(ILogger logger, OperacaoGateway operacao, Guid pedidoId, int tentativa);

    [LoggerMessage(EventId = 7015, EventName = "ErroPermanenteGateway", Level = LogLevel.Error,
        Message = "Erro permanente do gateway na {Operacao} do pedido {PedidoId}: HTTP {StatusHttp}, código {Codigo}. Sem nova tentativa; verifique a configuração ou o contrato.")]
    public static partial void ErroPermanente(ILogger logger, OperacaoGateway operacao, Guid pedidoId, int statusHttp, string codigo);

    [LoggerMessage(EventId = 7016, EventName = "CircuitoAberto", Level = LogLevel.Error,
        Message = "Circuito do gateway de pagamento aberto por {PausaSegundos} s, até {DisponivelEm:O}: nenhuma chamada até lá.")]
    public static partial void CircuitoAberto(ILogger logger, double pausaSegundos, DateTimeOffset disponivelEm);

    [LoggerMessage(EventId = 7017, EventName = "CircuitoMeioAberto", Level = LogLevel.Information,
        Message = "Circuito do gateway de pagamento meio-aberto: a próxima chamada testa a API.")]
    public static partial void CircuitoMeioAberto(ILogger logger);

    [LoggerMessage(EventId = 7018, EventName = "CircuitoFechado", Level = LogLevel.Information,
        Message = "Circuito do gateway de pagamento fechado: chamadas normais.")]
    public static partial void CircuitoFechado(ILogger logger);

    [LoggerMessage(EventId = 7020, EventName = "EstornosAdiados", Level = LogLevel.Warning,
        Message = "Rodada de estornos encerrada com o circuito do gateway aberto: {Quantidade} estorno(s) adiado(s), sem contar tentativa; disponível em {DisponivelEm:O}.")]
    public static partial void EstornosAdiados(ILogger logger, int quantidade, DateTimeOffset disponivelEm);
}
