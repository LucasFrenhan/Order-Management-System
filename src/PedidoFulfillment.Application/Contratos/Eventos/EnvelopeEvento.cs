namespace PedidoFulfillment.Application.Contratos.Eventos;

/// <summary>
/// Envelope comum dos eventos de domínio, versão 1 (REQ-06, FR-001). Schemas em
/// <c>specs/006-mensageria/contracts/eventos/</c>. Entrega pelo menos uma vez: deduplicar por
/// <see cref="IdEvento"/>.
/// </summary>
public sealed record EnvelopeEvento<T>
{
    public required Guid IdEvento { get; init; }

    /// <summary>Nome do evento no passado, também a chave de roteamento (ex.: <c>pedido.criado</c>).</summary>
    public required string Tipo { get; init; }

    public required int Versao { get; init; }

    public required DateTimeOffset OcorridoEm { get; init; }

    /// <summary>Identificador do pedido.</summary>
    public required Guid IdCorrelacao { get; init; }

    public required T Dados { get; init; }
}

public static class EnvelopeEvento
{
    /// <summary>Envelope novo da versão atual, com o tipo do catálogo para <typeparamref name="T"/>.</summary>
    public static EnvelopeEvento<T> Criar<T>(Guid idCorrelacao, T dados, DateTimeOffset instante) => new()
    {
        IdEvento = Guid.CreateVersion7(instante),
        Tipo = TiposEvento.TipoDe<T>(),
        Versao = TiposEvento.VersaoAtual,
        OcorridoEm = instante,
        IdCorrelacao = idCorrelacao,
        Dados = dados
    };
}
