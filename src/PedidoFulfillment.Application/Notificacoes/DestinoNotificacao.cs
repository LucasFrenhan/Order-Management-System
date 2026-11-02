namespace PedidoFulfillment.Application.Notificacoes;

/// <summary>
/// URL que recebe webhooks (REQ-18, FR-007 a FR-011, data-model). Do Cliente quando
/// <see cref="ClienteId"/> está preenchido; global, da operação, quando é nulo. Operacional, por
/// isso no Application, como o <c>PedidoReenvioDlq</c>.
/// </summary>
public sealed class DestinoNotificacao
{
    public const int TamanhoMaximoUrl = 2048;
    public const int TamanhoMaximoMotivo = 200;

    /// <summary>Nonce (12) + segredo cifrado (32) + tag (16), AES-GCM.</summary>
    public const int TamanhoSegredoCifrado = 60;

    public const string MotivoGone = "410 Gone";

    private DestinoNotificacao()
    {
        Url = "";
        SegredoCifrado = [];
    }

    public Guid Id { get; private set; }

    /// <summary>Dono, quando é de um Cliente; nulo quando é global.</summary>
    public Guid? ClienteId { get; private set; }

    public Guid CriadoPor { get; private set; }

    public string Url { get; private set; }

    public byte[] SegredoCifrado { get; private set; }

    public bool Ativo { get; private set; }

    public string? MotivoDesativacao { get; private set; }

    public DateTimeOffset? DesativadoEm { get; private set; }

    public int FalhasSeguidas { get; private set; }

    /// <summary>Também limita os avisos recebidos: só os gravados a partir daqui (research R-05 revista).</summary>
    public DateTimeOffset CriadoEm { get; private set; }

    public DateTimeOffset AtualizadoEm { get; private set; }

    public bool Global => ClienteId is null;

    /// <exception cref="ArgumentException">Autor vazio, URL vazia ou longa demais, ou segredo com tamanho errado.</exception>
    public static DestinoNotificacao Cadastrar(Guid? clienteId, Guid criadoPor, string url, byte[] segredoCifrado, DateTimeOffset instante)
    {
        ArgumentNullException.ThrowIfNull(segredoCifrado);
        if (segredoCifrado.Length != TamanhoSegredoCifrado)
        {
            throw new ArgumentException($"O segredo cifrado deve ter {TamanhoSegredoCifrado} bytes.", nameof(segredoCifrado));
        }

        var agora = GuardasNotificacoes.Utc(instante);
        return new DestinoNotificacao
        {
            Id = Guid.CreateVersion7(agora),
            ClienteId = clienteId is { } dono ? GuardasNotificacoes.Identificador(dono, nameof(clienteId)) : null,
            CriadoPor = GuardasNotificacoes.Identificador(criadoPor),
            Url = GuardasNotificacoes.Texto(url, TamanhoMaximoUrl),
            SegredoCifrado = segredoCifrado,
            Ativo = true,
            CriadoEm = agora,
            AtualizadoEm = agora
        };
    }

    /// <summary>Desativa com o motivo (entrega esgotada ou <c>410</c>). Devolve falso se já estava inativo.</summary>
    public bool Desativar(string motivo, DateTimeOffset instante)
    {
        if (!Ativo)
        {
            return false;
        }

        Ativo = false;
        MotivoDesativacao = GuardasNotificacoes.Texto(motivo, TamanhoMaximoMotivo);
        DesativadoEm = GuardasNotificacoes.Utc(instante);
        AtualizadoEm = DesativadoEm.Value;
        return true;
    }

    /// <summary>Volta a receber os avisos novos e zera as falhas. Devolve falso se já estava ativo (idempotente).</summary>
    public bool Reativar(DateTimeOffset instante)
    {
        if (Ativo)
        {
            return false;
        }

        Ativo = true;
        MotivoDesativacao = null;
        DesativadoEm = null;
        FalhasSeguidas = 0;
        AtualizadoEm = GuardasNotificacoes.Utc(instante);
        return true;
    }

    public void RegistrarFalha(DateTimeOffset instante)
    {
        FalhasSeguidas++;
        AtualizadoEm = GuardasNotificacoes.Utc(instante);
    }

    public void RegistrarSucesso(DateTimeOffset instante)
    {
        if (FalhasSeguidas == 0)
        {
            return;
        }

        FalhasSeguidas = 0;
        AtualizadoEm = GuardasNotificacoes.Utc(instante);
    }
}
