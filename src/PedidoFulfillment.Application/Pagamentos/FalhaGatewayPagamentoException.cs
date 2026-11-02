namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>Se repetir pode resolver (REQ-19, FR-010).</summary>
public enum TipoFalhaGateway
{
    /// <summary>Indisponível, limite de taxa, erro interno, em andamento, tempo esgotado ou conexão.</summary>
    Passageira = 1,

    /// <summary>Requisição inválida, credencial recusada, chave reutilizada ou resposta fora do contrato.</summary>
    Permanente
}

/// <summary>Operação chamada no gateway, para os logs e a exceção.</summary>
public enum OperacaoGateway
{
    Cobranca = 1,
    Estorno,
    Consulta
}

/// <summary>
/// A chamada ao gateway terminou sem resultado (REQ-19, FR-011 e FR-014): as tentativas se
/// esgotaram ou o erro é permanente. Nada é gravado; o chamador segue o caminho de falha de hoje.
/// A mensagem não traz chave de acesso, identificador de transação nem corpo (FR-021).
/// </summary>
public sealed class FalhaGatewayPagamentoException(TipoFalhaGateway tipo, OperacaoGateway operacao, string detalhe, Exception? innerException = null)
    : Exception($"Falha {(tipo == TipoFalhaGateway.Passageira ? "passageira" : "permanente")} do gateway de pagamento na {Nome(operacao)}: {detalhe}", innerException)
{
    public TipoFalhaGateway Tipo { get; } = tipo;

    public OperacaoGateway Operacao { get; } = operacao;

    private static string Nome(OperacaoGateway operacao) => operacao switch
    {
        OperacaoGateway.Cobranca => "cobrança",
        OperacaoGateway.Estorno => "estorno",
        _ => "consulta"
    };
}
