namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Cobrança, estorno e consulta no serviço de pagamento (REQ-09, research R-02; REQ-13, R-03;
/// REQ-19). No Worker, a implementação chama a API de pagamento pela rede; nos testes que não
/// exercitam a rede, um gateway em memória (REQ-19, FR-009a).
/// </summary>
/// <remarks>
/// O pedido é a chave de idempotência das operações: repetir a chamada para o mesmo pedido
/// devolve o mesmo resultado, sem cobrar nem estornar de novo (REQ-13, FR-013 e FR-014; REQ-19,
/// FR-017). Só resultados voltam como retorno; sem resultado, a chamada lança
/// <see cref="FalhaGatewayPagamentoException"/> ou, com o circuito aberto,
/// <see cref="GatewayPagamentoIndisponivelException"/>. O chamador não grava nada nesses casos.
/// </remarks>
public interface IGatewayPagamento
{
    Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken);

    /// <summary>Estorna o valor cobrado na transação <paramref name="identificadorTransacao"/> (REQ-13, FR-002).</summary>
    Task<ResultadoEstorno> EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken cancellationToken);

    /// <summary>A cobrança guardada pela chave do pedido, sem cobrar (REQ-19, FR-006 e FR-015).</summary>
    Task<ConsultaCobranca> ConsultarCobrancaAsync(Guid pedidoId, CancellationToken cancellationToken);
}

/// <summary>Resultado da cobrança: identificador da transação na aprovação, motivo na recusa.</summary>
public sealed record ResultadoCobranca(bool Aprovado, string? IdentificadorTransacao, string? Motivo)
{
    public static ResultadoCobranca Aprovada(string identificadorTransacao) => new(true, identificadorTransacao, null);

    public static ResultadoCobranca Recusada(string motivo) => new(false, null, motivo);
}

/// <summary>Resultado do estorno: identificador do estorno na confirmação, motivo na recusa (REQ-13, R-03).</summary>
public sealed record ResultadoEstorno(bool Confirmado, string? IdentificadorEstorno, string? Motivo)
{
    public static ResultadoEstorno Confirmar(string identificadorEstorno) => new(true, identificadorEstorno, null);

    public static ResultadoEstorno Recusar(string motivo) => new(false, null, motivo);
}

/// <summary>Situação de uma cobrança consultada pela chave (REQ-19, data-model › ConsultaCobranca).</summary>
public enum SituacaoConsultaCobranca
{
    /// <summary>A chave não existe no gateway: não houve cobrança.</summary>
    NaoEncontrada = 1,
    Aprovada,
    Recusada
}

/// <summary>Cobrança consultada: o identificador na aprovação, o motivo na recusa.</summary>
public sealed record ConsultaCobranca(SituacaoConsultaCobranca Situacao, string? IdentificadorTransacao, string? Motivo)
{
    public static ConsultaCobranca NaoEncontrada { get; } = new(SituacaoConsultaCobranca.NaoEncontrada, null, null);

    public static ConsultaCobranca Aprovada(string identificadorTransacao) => new(SituacaoConsultaCobranca.Aprovada, identificadorTransacao, null);

    public static ConsultaCobranca Recusada(string motivo) => new(SituacaoConsultaCobranca.Recusada, null, motivo);
}
