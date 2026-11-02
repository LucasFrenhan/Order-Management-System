namespace PedidoFulfillment.Application.Notificacoes;

/// <summary>Situação de uma entrega de webhook (REQ-18, data-model). Gravada como texto.</summary>
public enum SituacaoEntrega
{
    Pendente = 1,
    Entregue,
    Esgotada,
    Descartada
}

/// <summary>
/// O envio de um aviso para um destino (REQ-18, FR-012 a FR-018, data-model), com tentativas,
/// espera e situação próprias. Sai de <see cref="SituacaoEntrega.Pendente"/> uma vez só.
/// </summary>
public sealed class EntregaNotificacao
{
    public const int TamanhoMaximoErro = 200;

    private EntregaNotificacao()
    {
    }

    public long Id { get; private set; }

    /// <summary>Nulo depois de o destino ser removido; as entregas finalizadas ficam até a limpeza.</summary>
    public Guid? DestinoId { get; private set; }

    public long AvisoId { get; private set; }

    public SituacaoEntrega Situacao { get; private set; }

    public int Tentativas { get; private set; }

    public DateTimeOffset ProximaTentativaEm { get; private set; }

    /// <summary>Início da janela de novas tentativas (FR-015).</summary>
    public DateTimeOffset? PrimeiraTentativaEm { get; private set; }

    public int? UltimoCodigoHttp { get; private set; }

    /// <summary>Tipo do erro (tempo esgotado, endereço recusado, conexão); sem URL completa nem corpo da resposta.</summary>
    public string? UltimoErro { get; private set; }

    public DateTimeOffset CriadaEm { get; private set; }

    public DateTimeOffset? FinalizadaEm { get; private set; }

    /// <summary>Contexto W3C de quem gravou o aviso (REQ-16), para o link do span de envio.</summary>
    public string? ContextoRastreio { get; private set; }

    public static EntregaNotificacao Criar(Guid destinoId, long avisoId, string? contextoRastreio, DateTimeOffset instante)
    {
        var agora = GuardasNotificacoes.Utc(instante);
        return new EntregaNotificacao
        {
            DestinoId = GuardasNotificacoes.Identificador(destinoId),
            AvisoId = avisoId > 0 ? avisoId : throw new ArgumentOutOfRangeException(nameof(avisoId)),
            Situacao = SituacaoEntrega.Pendente,
            ProximaTentativaEm = agora,
            CriadaEm = agora,
            ContextoRastreio = contextoRastreio
        };
    }

    public void MarcarEntregue(int codigoHttp, DateTimeOffset instante)
    {
        RegistrarTentativa(codigoHttp, null, instante);
        Finalizar(SituacaoEntrega.Entregue, instante);
    }

    /// <summary>Falha com nova tentativa marcada para <paramref name="proxima"/>.</summary>
    public void RegistrarFalha(int? codigoHttp, string erro, DateTimeOffset proxima, DateTimeOffset instante)
    {
        RegistrarTentativa(codigoHttp, erro, instante);
        ProximaTentativaEm = GuardasNotificacoes.Utc(proxima);
    }

    /// <summary>Falha final: 24 h de tentativas ou <c>410 Gone</c>.</summary>
    public void Esgotar(int? codigoHttp, string erro, DateTimeOffset instante)
    {
        RegistrarTentativa(codigoHttp, erro, instante);
        Finalizar(SituacaoEntrega.Esgotada, instante);
    }

    /// <summary>O destino foi desativado ou removido. Devolve falso se a entrega já tinha terminado.</summary>
    public bool Descartar(DateTimeOffset instante)
    {
        if (Situacao != SituacaoEntrega.Pendente)
        {
            return false;
        }

        Finalizar(SituacaoEntrega.Descartada, instante);
        return true;
    }

    private void RegistrarTentativa(int? codigoHttp, string? erro, DateTimeOffset instante)
    {
        ExigirPendente();
        var agora = GuardasNotificacoes.Utc(instante);
        Tentativas++;
        PrimeiraTentativaEm ??= agora;
        UltimoCodigoHttp = codigoHttp;
        UltimoErro = GuardasNotificacoes.TextoOpcional(erro is { Length: > TamanhoMaximoErro } ? erro[..TamanhoMaximoErro] : erro, TamanhoMaximoErro);
    }

    private void Finalizar(SituacaoEntrega situacao, DateTimeOffset instante)
    {
        ExigirPendente();
        Situacao = situacao;
        FinalizadaEm = GuardasNotificacoes.Utc(instante);
    }

    private void ExigirPendente()
    {
        if (Situacao != SituacaoEntrega.Pendente)
        {
            throw new InvalidOperationException($"A entrega {Id} já terminou ({Situacao}).");
        }
    }
}
