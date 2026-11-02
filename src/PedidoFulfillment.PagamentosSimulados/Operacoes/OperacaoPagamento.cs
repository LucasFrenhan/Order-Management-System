namespace PedidoFulfillment.PagamentosSimulados.Operacoes;

/// <summary>Cobrança ou estorno: espaços de chave separados (contrato › Estorno).</summary>
public enum TipoOperacao
{
    Cobranca = 1,
    Estorno
}

/// <summary>Situação de uma operação guardada (data-model › OperacaoPagamento).</summary>
public enum SituacaoOperacao
{
    EmAndamento = 1,
    Aprovada,
    Recusada,
    Confirmado,
    Recusado
}

/// <summary>
/// Uma cobrança ou um estorno guardado em memória pela chave do pedido (REQ-19, FR-005;
/// data-model › OperacaoPagamento). <see cref="Efetivacoes"/> conta a decisão de negócio (0 ou 1),
/// e <see cref="Chamadas"/>, todas as chamadas recebidas com a chave.
/// </summary>
public sealed class OperacaoPagamento
{
    private readonly Lock trava = new();
    private int chamadas;

    public OperacaoPagamento(TipoOperacao tipo, Guid chave, decimal valor, string? identificadorCobranca)
    {
        Tipo = tipo;
        Chave = chave;
        Valor = valor;
        IdentificadorCobranca = identificadorCobranca;
        Situacao = SituacaoOperacao.EmAndamento;
        chamadas = 1;
    }

    public TipoOperacao Tipo { get; }

    public Guid Chave { get; }

    public decimal Valor { get; }

    /// <summary>Só no estorno: o <c>PAG-…</c> informado.</summary>
    public string? IdentificadorCobranca { get; }

    public SituacaoOperacao Situacao { get; private set; }

    /// <summary><c>PAG-…</c> ou <c>EST-…</c>; nulo na recusa.</summary>
    public string? Identificador { get; private set; }

    public string? Motivo { get; private set; }

    public DateTimeOffset? ProcessadaEm { get; private set; }

    public int Efetivacoes { get; private set; }

    public int Chamadas => Volatile.Read(ref chamadas);

    public bool Concluida
    {
        get
        {
            lock (trava)
            {
                return Situacao != SituacaoOperacao.EmAndamento;
            }
        }
    }

    public void ContarChamada() => Interlocked.Increment(ref chamadas);

    /// <summary>Aprovação da cobrança ou confirmação do estorno; uma única vez.</summary>
    public void Efetivar(DateTimeOffset instante)
    {
        lock (trava)
        {
            ExigirEmAndamento();
            Situacao = Tipo == TipoOperacao.Cobranca ? SituacaoOperacao.Aprovada : SituacaoOperacao.Confirmado;
            Identificador = $"{(Tipo == TipoOperacao.Cobranca ? "PAG" : "EST")}-{Guid.CreateVersion7():N}";
            ProcessadaEm = instante;
            Efetivacoes++;
        }
    }

    /// <summary>Recusa de negócio, também uma decisão guardada pela chave.</summary>
    public void Recusar(string motivo, DateTimeOffset instante)
    {
        lock (trava)
        {
            ExigirEmAndamento();
            Situacao = Tipo == TipoOperacao.Cobranca ? SituacaoOperacao.Recusada : SituacaoOperacao.Recusado;
            Motivo = motivo;
            ProcessadaEm = instante;
            Efetivacoes++;
        }
    }

    private void ExigirEmAndamento()
    {
        if (Situacao != SituacaoOperacao.EmAndamento)
        {
            throw new InvalidOperationException("A operação já foi concluída.");
        }
    }
}
