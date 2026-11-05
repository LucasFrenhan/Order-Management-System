namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>O que o token de uso único autoriza (REQ-24, data-model).</summary>
public enum FinalidadeTokenUsoUnico
{
    RecuperacaoSenha,
    ConfirmacaoEmail
}

/// <summary>Como terminou a apresentação de um token de renovação (REQ-24, R-02).</summary>
public enum SituacaoRotacao
{
    Renovada,

    /// <summary>Inexistente, vencido ou de uma sessão já encerrada.</summary>
    Recusada,

    /// <summary>Já usado, e esta apresentação encerrou a sessão.</summary>
    Reutilizada
}

/// <param name="UsuarioId">O dono da sessão, em <see cref="SituacaoRotacao.Renovada"/> e <see cref="SituacaoRotacao.Reutilizada"/>.</param>
/// <param name="TokenExpiraEm">A validade do token novo, em <see cref="SituacaoRotacao.Renovada"/>.</param>
public sealed record ResultadoRotacao(SituacaoRotacao Situacao, Guid? UsuarioId = null, DateTimeOffset? TokenExpiraEm = null);

/// <summary>
/// Sessões e tokens de autenticação (REQ-24, R-02). Só recebe hashes. As validades vêm por parâmetro
/// porque o Worker também usa esta porta, e a configuração de sessões só existe na API. Usa o
/// contexto da requisição: com uma transação aberta, as operações entram nela.
/// </summary>
public interface IArmazenamentoSessoes
{
    /// <summary>Cria a sessão com o primeiro token e devolve a validade dele.</summary>
    Task<DateTimeOffset> CriarSessaoAsync(Guid usuarioId, byte[] hashToken, DateTimeOffset agora, ConfiguracaoSessoes validades, CancellationToken cancellationToken);

    /// <summary>
    /// Troca o token apresentado pelo novo, de forma atômica: de duas apresentações simultâneas, só
    /// uma renova, e a outra é reutilização, que encerra a sessão (FR-002, FR-004).
    /// </summary>
    Task<ResultadoRotacao> RotacionarAsync(byte[] hashApresentado, byte[] hashNovo, DateTimeOffset agora, ConfiguracaoSessoes validades, CancellationToken cancellationToken);

    /// <summary>Encerra a sessão de um token ainda válido e devolve o dono; senão, nulo (FR-008).</summary>
    Task<Guid?> EncerrarSessaoAsync(byte[] hashToken, DateTimeOffset agora, CancellationToken cancellationToken);

    /// <summary>Encerra todas as sessões da conta (FR-011).</summary>
    Task EncerrarSessoesDaContaAsync(Guid usuarioId, DateTimeOffset agora, CancellationToken cancellationToken);

    /// <summary>Grava o token novo e apaga os anteriores da mesma conta e finalidade (FR-014).</summary>
    Task CriarTokenUsoUnicoAsync(Guid usuarioId, FinalidadeTokenUsoUnico finalidade, byte[] hashToken, DateTimeOffset expiraEm, CancellationToken cancellationToken);

    /// <summary>Consome o token de forma atômica e devolve o dono; inválido, vencido ou usado devolve nulo.</summary>
    Task<Guid?> ConsumirTokenUsoUnicoAsync(FinalidadeTokenUsoUnico finalidade, byte[] hashToken, DateTimeOffset agora, CancellationToken cancellationToken);
}
