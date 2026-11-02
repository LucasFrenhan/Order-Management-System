namespace PedidoFulfillment.Application.Notificacoes;

/// <summary>
/// Uma notificação pronta para sair (REQ-18, research R-13 e data-model): gravada na mesma
/// transação da mudança de status ou da confirmação do estorno, lida pela API (SSE) e pelo Worker
/// (webhooks) por cursor de <c>rowversion</c>. Só de inclusão.
/// </summary>
public sealed class AvisoPedido
{
    public const int TamanhoMaximoTipo = 50;

    private AvisoPedido()
    {
        Tipo = "";
        Conteudo = "";
    }

    public long Id { get; private set; }

    /// <summary>Igual ao <c>IdEvento</c> do evento de origem; <c>webhook-id</c> e <c>id</c> do SSE.</summary>
    public Guid IdNotificacao { get; private set; }

    public string Tipo { get; private set; }

    public Guid PedidoId { get; private set; }

    /// <summary>Dono do pedido: filtra o SSE e escolhe os destinos. Não vai no corpo.</summary>
    public Guid ClienteId { get; private set; }

    /// <summary>Corpo <c>notificacao.v1</c> serializado, enviado byte a byte (a assinatura é sobre ele).</summary>
    public string Conteudo { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    /// <exception cref="ArgumentException">Identificador vazio, tipo vazio ou longo demais, ou conteúdo vazio.</exception>
    public static AvisoPedido Criar(Guid idNotificacao, string tipo, Guid pedidoId, Guid clienteId, string conteudo, DateTimeOffset instante) => new()
    {
        IdNotificacao = GuardasNotificacoes.Identificador(idNotificacao),
        Tipo = GuardasNotificacoes.Texto(tipo, TamanhoMaximoTipo),
        PedidoId = GuardasNotificacoes.Identificador(pedidoId),
        ClienteId = GuardasNotificacoes.Identificador(clienteId),
        Conteudo = string.IsNullOrWhiteSpace(conteudo) ? throw new ArgumentException("O conteúdo é obrigatório.", nameof(conteudo)) : conteudo,
        CriadoEm = GuardasNotificacoes.Utc(instante)
    };
}
