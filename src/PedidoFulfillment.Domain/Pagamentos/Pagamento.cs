using PedidoFulfillment.Domain.Comum;

namespace PedidoFulfillment.Domain.Pagamentos;

/// <summary>
/// Resultado do pagamento de um pedido; no máximo um por pedido (FR-012).
/// </summary>
public sealed class Pagamento
{
    public const int TamanhoMaximoIdentificadorTransacao = 100;

    public Pagamento(Guid pedidoId, decimal valor, DateTimeOffset instante)
    {
        Id = Guid.CreateVersion7();
        PedidoId = Guardas.Identificador(pedidoId);
        Valor = Guardas.Dinheiro(valor);
        Situacao = SituacaoPagamento.Pendente;
        CriadoEm = Guardas.Utc(instante);
        AtualizadoEm = CriadoEm;
    }

    private Pagamento()
    {
    }

    public Guid Id { get; private set; }

    public Guid PedidoId { get; private set; }

    public decimal Valor { get; private set; }

    public SituacaoPagamento Situacao { get; private set; }

    /// <summary>Referência da transação no serviço de pagamento externo.</summary>
    public string? IdentificadorTransacao { get; private set; }

    public DateTimeOffset? ProcessadoEm { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public DateTimeOffset AtualizadoEm { get; private set; }

    public void RegistrarResultado(SituacaoPagamento situacao, string? identificadorTransacao, DateTimeOffset instante)
    {
        if (situacao is not (SituacaoPagamento.Aprovado or SituacaoPagamento.Recusado))
        {
            throw new ArgumentOutOfRangeException(nameof(situacao), situacao, "O resultado deve ser Aprovado ou Recusado.");
        }

        var transacao = Guardas.TextoOpcional(identificadorTransacao, TamanhoMaximoIdentificadorTransacao);
        AtualizadoEm = Guardas.Utc(instante);
        ProcessadoEm = instante;
        Situacao = situacao;
        IdentificadorTransacao = transacao;
    }
}
