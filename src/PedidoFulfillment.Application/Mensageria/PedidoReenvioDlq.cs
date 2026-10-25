namespace PedidoFulfillment.Application.Mensageria;

/// <summary>Situação de um pedido de reenvio da DLQ (REQ-14, data-model). Gravada como texto.</summary>
public enum SituacaoReenvioDlq
{
    Pendente = 1,
    EmExecucao,
    Concluido,
    Falhou
}

/// <summary>Resultado de uma execução: as mensagens reenviadas, as bloqueadas pelo limite e as fora do filtro.</summary>
public readonly record struct ContagensReenvio(int Reenviadas, int BloqueadasPeloLimite, int ForaDoFiltro);

/// <summary>
/// Pedido de reenvio das mensagens da DLQ de um consumidor (REQ-14, R-03 e data-model): a API o
/// registra com o Admin autenticado como autor, e o Worker o executa no broker. É também o
/// histórico de quem reenviou o quê. Operacional, por isso no Application (como o
/// <c>RegistroIdempotencia</c> do REQ-05).
/// </summary>
public sealed class PedidoReenvioDlq
{
    public const int TamanhoMaximoConsumidor = 100;
    public const int TamanhoMaximoTipoEvento = 100;
    public const int TamanhoMaximoTrechoMotivo = 200;
    public const int TamanhoMaximoMotivoFalha = 500;

    private PedidoReenvioDlq()
    {
        Consumidor = "";
    }

    public Guid Id { get; private set; }

    public Guid AutorId { get; private set; }

    public string Consumidor { get; private set; }

    public string? TipoEvento { get; private set; }

    public DateTimeOffset? FalhouDe { get; private set; }

    public DateTimeOffset? FalhouAte { get; private set; }

    public string? TrechoMotivo { get; private set; }

    public SituacaoReenvioDlq Situacao { get; private set; }

    public DateTimeOffset RegistradoEm { get; private set; }

    public DateTimeOffset? IniciadoEm { get; private set; }

    public DateTimeOffset? ConcluidoEm { get; private set; }

    public int Reenviadas { get; private set; }

    public int BloqueadasPeloLimite { get; private set; }

    public int ForaDoFiltro { get; private set; }

    public string? MotivoFalha { get; private set; }

    public FiltroReenvio Filtro => new(TipoEvento, FalhouDe, FalhouAte, TrechoMotivo);

    /// <exception cref="ArgumentException">Autor vazio, consumidor desconhecido, período invertido ou campo acima do tamanho.</exception>
    public static PedidoReenvioDlq Registrar(Guid autorId, string consumidor, FiltroReenvio filtro, DateTimeOffset instante)
    {
        ArgumentNullException.ThrowIfNull(filtro);
        if (autorId == Guid.Empty)
        {
            throw new ArgumentException("O autor é obrigatório.", nameof(autorId));
        }

        if (!ConsumidoresConhecidos.Conhecido(consumidor))
        {
            throw new ArgumentException($"O consumidor {consumidor} não é conhecido.", nameof(consumidor));
        }

        if (filtro.FalhouDe is { } de && filtro.FalhouAte is { } ate && ate < de)
        {
            throw new ArgumentException("O fim do período deve ser igual ou posterior ao início.", nameof(filtro));
        }

        return new PedidoReenvioDlq
        {
            Id = Guid.CreateVersion7(),
            AutorId = autorId,
            Consumidor = consumidor,
            TipoEvento = Aparar(filtro.TipoEvento, TamanhoMaximoTipoEvento, nameof(filtro.TipoEvento)),
            FalhouDe = filtro.FalhouDe,
            FalhouAte = filtro.FalhouAte,
            TrechoMotivo = Aparar(filtro.TrechoMotivo, TamanhoMaximoTrechoMotivo, nameof(filtro.TrechoMotivo)),
            Situacao = SituacaoReenvioDlq.Pendente,
            RegistradoEm = instante
        };
    }

    /// <summary>Pendente → EmExecucao; uma execução que caiu (EmExecucao) recomeça.</summary>
    /// <exception cref="InvalidOperationException">O pedido já terminou.</exception>
    public void Iniciar(DateTimeOffset instante)
    {
        if (Situacao is not (SituacaoReenvioDlq.Pendente or SituacaoReenvioDlq.EmExecucao))
        {
            throw new InvalidOperationException($"O pedido de reenvio {Id} já terminou: {Situacao}.");
        }

        Situacao = SituacaoReenvioDlq.EmExecucao;
        IniciadoEm = instante;
    }

    /// <exception cref="InvalidOperationException">O pedido não está em execução.</exception>
    public void Concluir(ContagensReenvio contagens, DateTimeOffset instante) =>
        Terminar(SituacaoReenvioDlq.Concluido, contagens, null, instante);

    /// <summary>O broker caiu no meio: as contagens são as parciais até a queda.</summary>
    /// <exception cref="InvalidOperationException">O pedido não está em execução.</exception>
    public void Falhar(ContagensReenvio contagens, string motivo, DateTimeOffset instante)
    {
        var aparado = motivo?.Trim() ?? "";
        Terminar(SituacaoReenvioDlq.Falhou, contagens, aparado.Length > TamanhoMaximoMotivoFalha ? aparado[..TamanhoMaximoMotivoFalha] : aparado, instante);
    }

    private void Terminar(SituacaoReenvioDlq situacao, ContagensReenvio contagens, string? motivo, DateTimeOffset instante)
    {
        if (Situacao != SituacaoReenvioDlq.EmExecucao)
        {
            throw new InvalidOperationException($"O pedido de reenvio {Id} não está em execução: {Situacao}.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(contagens.Reenviadas, nameof(contagens));
        ArgumentOutOfRangeException.ThrowIfNegative(contagens.BloqueadasPeloLimite, nameof(contagens));
        ArgumentOutOfRangeException.ThrowIfNegative(contagens.ForaDoFiltro, nameof(contagens));
        Situacao = situacao;
        (Reenviadas, BloqueadasPeloLimite, ForaDoFiltro) = (contagens.Reenviadas, contagens.BloqueadasPeloLimite, contagens.ForaDoFiltro);
        MotivoFalha = motivo;
        ConcluidoEm = instante;
    }

    private static string? Aparar(string? valor, int maximo, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return null;
        }

        var aparado = valor.Trim();
        return aparado.Length <= maximo ? aparado : throw new ArgumentException($"O campo deve ter até {maximo} caracteres.", campo);
    }
}
