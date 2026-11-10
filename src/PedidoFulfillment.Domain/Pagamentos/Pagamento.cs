using PedidoFulfillment.Domain.Comum;

namespace PedidoFulfillment.Domain.Pagamentos;

/// <summary>
/// Resultado do pagamento de um pedido; no máximo um por pedido (FR-012).
/// </summary>
public sealed class Pagamento
{
    public const int TamanhoMaximoIdentificadorTransacao = 100;

    public const int TamanhoMaximoIdentificadorEstorno = 100;

    /// <summary>Último erro do estorno, só por categoria (REQ-28, R-05).</summary>
    public const int TamanhoMaximoUltimoErroEstorno = 200;

    public const int TamanhoMinimoMotivoBaixa = 10;

    public const int TamanhoMaximoMotivoBaixa = 500;

    public const int TamanhoMaximoReferenciaExternaBaixa = 100;

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

    /// <summary>Texto da última falha, sem dado do gateway (REQ-28, R-05).</summary>
    public string? UltimoErroEstorno { get; private set; }

    /// <summary>Tentativa forçada pedida pela operação e ainda não feita (REQ-28, R-04).</summary>
    public DateTimeOffset? TentativaEstornoForcadaEm { get; private set; }

    /// <summary>Quando o estorno passou do limite de alerta e o evento saiu; só passa de nulo a preenchido (REQ-28, R-06).</summary>
    public DateTimeOffset? EstornoAtrasadoEm { get; private set; }

    public DateTimeOffset? EstornoBaixadoEm { get; private set; }

    /// <summary>Admin que deu a baixa (REQ-28, FR-007).</summary>
    public Guid? EstornoBaixadoPorId { get; private set; }

    public string? MotivoBaixaEstorno { get; private set; }

    public string? ReferenciaExternaBaixaEstorno { get; private set; }

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

    /// <summary>
    /// Mais uma tentativa de estorno falhou; a situação continua EstornoPendente (FR-003). Guarda o
    /// erro e retira a marca de tentativa forçada, que foi feita (REQ-28, FR-013).
    /// </summary>
    /// <exception cref="InvalidOperationException">O estorno não está pendente.</exception>
    /// <exception cref="ArgumentException">Erro vazio ou acima do tamanho.</exception>
    public void RegistrarFalhaEstorno(DateTimeOffset instante, string erro)
    {
        Guardas.Utc(instante);
        var texto = Guardas.Texto(erro, TamanhoMaximoUltimoErroEstorno);
        ExigirSituacao(SituacaoPagamento.EstornoPendente, "registrar falha de estorno");
        TentativasEstorno++;
        UltimaFalhaEstornoEm = instante;
        UltimoErroEstorno = texto;
        TentativaEstornoForcadaEm = null;
        AtualizadoEm = instante;
    }

    /// <summary>A operação pediu uma tentativa agora (REQ-28, FR-011); um segundo pedido não muda nada (FR-014).</summary>
    /// <returns><see langword="true"/> se a marca foi gravada agora.</returns>
    /// <exception cref="InvalidOperationException">O estorno não está pendente.</exception>
    public bool PedirTentativaEstorno(DateTimeOffset instante)
    {
        Guardas.Utc(instante);
        ExigirSituacao(SituacaoPagamento.EstornoPendente, "pedir tentativa de estorno");
        if (TentativaEstornoForcadaEm is not null)
        {
            return false;
        }

        TentativaEstornoForcadaEm = instante;
        AtualizadoEm = instante;
        return true;
    }

    /// <summary>O estorno passou do limite de alerta (REQ-28, FR-016): marca uma única vez.</summary>
    /// <returns><see langword="true"/> se a marca foi gravada agora, e o evento deve sair.</returns>
    /// <exception cref="InvalidOperationException">O estorno não está pendente.</exception>
    public bool MarcarEstornoAtrasado(DateTimeOffset instante)
    {
        Guardas.Utc(instante);
        ExigirSituacao(SituacaoPagamento.EstornoPendente, "marcar o estorno como atrasado");
        if (EstornoAtrasadoEm is not null)
        {
            return false;
        }

        EstornoAtrasadoEm = instante;
        AtualizadoEm = instante;
        return true;
    }

    /// <summary>
    /// EstornoPendente → EstornoBaixado: o Admin resolveu o estorno fora do sistema, sem o gateway
    /// (REQ-28, FR-006 e FR-007).
    /// </summary>
    /// <exception cref="InvalidOperationException">O estorno não está pendente.</exception>
    /// <exception cref="ArgumentException">Autor vazio, motivo fora de 10 a 500 caracteres ou referência acima de 100.</exception>
    public void DarBaixaEstorno(Guid autorId, string motivo, string? referenciaExterna, DateTimeOffset instante)
    {
        Guardas.Utc(instante);
        var autor = Guardas.Identificador(autorId);
        var texto = Guardas.Texto(motivo, TamanhoMaximoMotivoBaixa);
        if (texto.Length < TamanhoMinimoMotivoBaixa)
        {
            throw new ArgumentException($"O motivo deve ter ao menos {TamanhoMinimoMotivoBaixa} caracteres.", nameof(motivo));
        }

        var referencia = Guardas.TextoOpcional(referenciaExterna, TamanhoMaximoReferenciaExternaBaixa);
        ExigirSituacao(SituacaoPagamento.EstornoPendente, "dar baixa no estorno");
        Situacao = SituacaoPagamento.EstornoBaixado;
        EstornoBaixadoEm = instante;
        EstornoBaixadoPorId = autor;
        MotivoBaixaEstorno = texto;
        ReferenciaExternaBaixaEstorno = referencia;
        TentativaEstornoForcadaEm = null;
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
        TentativaEstornoForcadaEm = null;
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
