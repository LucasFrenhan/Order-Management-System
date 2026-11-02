using Microsoft.Extensions.Logging;

namespace PedidoFulfillment.Infrastructure.Notificacoes;

/// <summary>
/// Eventos das notificações (REQ-18, research R-19; FR-028). A URL do destino só com esquema e
/// host (<see cref="HostDestino"/>); nunca o segredo, o corpo enviado ou o corpo da resposta.
/// </summary>
internal static partial class EventosNotificacao
{
    /// <summary>A URL reduzida a esquema, host e porta, para os logs.</summary>
    public static string HostDestino(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : "(url inválida)";

    // 10001 a 10003 (cadastro, remoção e reativação) ficam no ServicoDestinosNotificacao, no Application.

    [LoggerMessage(EventId = 10004, EventName = "DestinoNotificacaoDesativado", Level = LogLevel.Warning,
        Message = "Destino de notificação {DestinoId} ({Host}) desativado: {Motivo}; {Descartadas} entrega(s) pendente(s) descartada(s).")]
    public static partial void DestinoDesativado(ILogger logger, Guid destinoId, string host, string motivo, int descartadas);

    [LoggerMessage(EventId = 10005, EventName = "EntregasNotificacaoCriadas", Level = LogLevel.Information,
        Message = "Distribuição dos avisos: {Avisos} aviso(s), {Entregas} entrega(s) criada(s).")]
    public static partial void EntregasCriadas(ILogger logger, int avisos, int entregas);

    [LoggerMessage(EventId = 10006, EventName = "NotificacaoEntregue", Level = LogLevel.Information,
        Message = "Notificação {IdNotificacao} entregue ao destino {DestinoId} ({Host}) com {CodigoHttp} na tentativa {Tentativa}.")]
    public static partial void Entregue(ILogger logger, Guid idNotificacao, Guid destinoId, string host, int codigoHttp, int tentativa);

    [LoggerMessage(EventId = 10007, EventName = "FalhaEntregaNotificacao", Level = LogLevel.Warning,
        Message = "Falha na entrega da notificação {IdNotificacao} ao destino {DestinoId} ({Host}), tentativa {Tentativa}: {Erro}. Próxima em {ProximaTentativaEm}.")]
    public static partial void Falha(ILogger logger, Guid idNotificacao, Guid destinoId, string host, int tentativa, string erro, DateTimeOffset proximaTentativaEm);

    [LoggerMessage(EventId = 10008, EventName = "EntregaNotificacaoEsgotada", Level = LogLevel.Error,
        Message = "Entrega da notificação {IdNotificacao} ao destino {DestinoId} ({Host}) esgotada depois de {Tentativas} tentativa(s): {Erro}.")]
    public static partial void Esgotada(ILogger logger, Guid idNotificacao, Guid destinoId, string host, int tentativas, string erro);

    [LoggerMessage(EventId = 10009, EventName = "EnderecoNotificacaoRecusado", Level = LogLevel.Warning,
        Message = "Endereço recusado para o host {Host}: resolve para endereço interno ({Endereco}).")]
    public static partial void EnderecoRecusado(ILogger logger, string host, string endereco);

    [LoggerMessage(EventId = 10010, EventName = "ResumoEnvioNotificacoes",
        Message = "Rodada de notificações: {Entregues} entregue(s), {Falhas} falha(s), {Esgotadas} esgotada(s).")]
    public static partial void ResumoEnvio(ILogger logger, LogLevel nivel, int entregues, int falhas, int esgotadas);

    [LoggerMessage(EventId = 10011, EventName = "ConexaoSseAberta", Level = LogLevel.Information,
        Message = "Conexão de eventos aberta por {UsuarioId} (pedido {PedidoId}, reconexão: {Reconexao}).")]
    public static partial void ConexaoAberta(ILogger logger, Guid usuarioId, Guid? pedidoId, bool reconexao);

    [LoggerMessage(EventId = 10012, EventName = "ConexaoSseEncerrada", Level = LogLevel.Information,
        Message = "Conexão de eventos de {UsuarioId} encerrada: {Motivo}.")]
    public static partial void ConexaoEncerrada(ILogger logger, Guid usuarioId, string motivo);

    [LoggerMessage(EventId = 10013, EventName = "ConexaoSseRecusada", Level = LogLevel.Warning,
        Message = "Conexão de eventos de {UsuarioId} recusada: limite de {Limite} conexões atingido.")]
    public static partial void ConexaoRecusada(ILogger logger, Guid usuarioId, int limite);

    [LoggerMessage(EventId = 10014, EventName = "FalhaLeituraAvisos", Level = LogLevel.Warning,
        Message = "Falha na leitura dos avisos ({Leitor}): {Erro}. Será tentada de novo.")]
    public static partial void FalhaLeitura(ILogger logger, string leitor, string erro);
}
