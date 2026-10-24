using PedidoFulfillment.Domain.Comum;

namespace PedidoFulfillment.Domain.Pagamentos;

/// <summary>
/// Resultado do pagamento de um pedido; no máximo um por pedido (FR-012).
/// </summary>
public sealed class Pagamento
{
    public const int TamanhoMaximoIdentificadorTransacao = 100;

    public const int TamanhoMaximoIdentificadorEstorno = 100;

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

    /// <summary>Quando o cancelamento pediu o estorno (REQ-13, R-02); ordena a fila e mede o alerta.</summary>
    public DateTimeOffset? EstornoSolicitadoEm { get; private set; }

    /// <summary>Referência do estorno no serviço de pagamento externo, na confirmação.</summary>
    public string? IdentificadorEstorno { get; private set; }

    public DateTimeOffset? EstornadoEm { get; private set; }

    /// <summary>Tentativas de estorno que falharam; gravadas para o alerta sobreviver a reinícios (R-02).</summary>
    public int TentativasEstorno { get; private set; }

    public DateTimeOffset? UltimaFalhaEstornoEm { get; private set; }

    /// <summary>Aprovado → EstornoPendente (REQ-13, FR-001). Só o <c>Pedido</c> chama, junto com o cancelamento.</summary>
    /// <exception cref="InvalidOperationException">O pagamento não está aprovado.</exception>
    public void SolicitarEstorno(DateTimeOffset instante)
    {
        Guardas.Utc(instante);
        ExigirSituacao(SituacaoPagamento.Aprovado, "pedir o estorno");
        Situacao = SituacaoPagamento.EstornoPendente;
        EstornoSolicitadoEm = instante;
        AtualizadoEm = instante;
    }

    /// <summary>Mais uma tentativa de estorno falhou; a situação continua EstornoPendente (FR-003).</summary>
    /// <exception cref="InvalidOperationException">O estorno não está pendente.</exception>
    public void RegistrarFalhaEstorno(DateTimeOffset instante)
    {
        Guardas.Utc(instante);
        ExigirSituacao(SituacaoPagamento.EstornoPendente, "registrar falha de estorno");
        TentativasEstorno++;
        UltimaFalhaEstornoEm = instante;
        AtualizadoEm = instante;
    }

    /// <summary>EstornoPendente → Estornado, com a referência do gateway (FR-002).</summary>
    /// <exception cref="InvalidOperationException">O estorno não está pendente.</exception>
    /// <exception cref="ArgumentException">Identificador vazio ou acima do tamanho.</exception>
    public void ConfirmarEstorno(string identificadorEstorno, DateTimeOffset instante)
    {
        var identificador = Guardas.Texto(identificadorEstorno, TamanhoMaximoIdentificadorEstorno);
        Guardas.Utc(instante);
        ExigirSituacao(SituacaoPagamento.EstornoPendente, "confirmar o estorno");
        Situacao = SituacaoPagamento.Estornado;
        IdentificadorEstorno = identificador;
        EstornadoEm = instante;
        AtualizadoEm = instante;
    }

    public void RegistrarResultado(SituacaoPagamento situacao, string? identificadorTransacao, DateTimeOffset instante)
    {
        if (situacao is not (SituacaoPagamento.Aprovado or SituacaoPagamento.Recusado or SituacaoPagamento.NaoProcessado))
        {
            throw new ArgumentOutOfRangeException(nameof(situacao), situacao, "O resultado deve ser Aprovado, Recusado ou NaoProcessado.");
        }

        var transacao = Guardas.TextoOpcional(identificadorTransacao, TamanhoMaximoIdentificadorTransacao);
        AtualizadoEm = Guardas.Utc(instante);
        ProcessadoEm = instante;
        Situacao = situacao;
        IdentificadorTransacao = transacao;
    }

    private void ExigirSituacao(SituacaoPagamento esperada, string acao)
    {
        if (Situacao != esperada)
        {
            throw new InvalidOperationException($"Não é possível {acao}: o pagamento do pedido {PedidoId} está {Situacao}.");
        }
    }
}
