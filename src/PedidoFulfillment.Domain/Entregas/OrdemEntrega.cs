using PedidoFulfillment.Domain.Comum;

namespace PedidoFulfillment.Domain.Entregas;

/// <summary>
/// Registro de logística de um pedido pago; no máximo uma por pedido (FR-013). Transportadora,
/// código de rastreio e data de despacho chegam com o despacho.
/// </summary>
public sealed class OrdemEntrega
{
    public const int TamanhoMaximoTransportadora = 100;
    public const int TamanhoMaximoCodigoRastreio = 50;

    public OrdemEntrega(Guid pedidoId, DateTimeOffset instante)
    {
        Id = Guid.CreateVersion7();
        PedidoId = Guardas.Identificador(pedidoId);
        CriadoEm = Guardas.Utc(instante);
        AtualizadoEm = CriadoEm;
    }

    private OrdemEntrega()
    {
    }

    public Guid Id { get; private set; }

    public Guid PedidoId { get; private set; }

    public string? Transportadora { get; private set; }

    /// <summary>Único dentro da mesma transportadora, quando informado.</summary>
    public string? CodigoRastreio { get; private set; }

    public DateTimeOffset? DespachadoEm { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public DateTimeOffset AtualizadoEm { get; private set; }

    public void RegistrarDespacho(string transportadora, string codigoRastreio, DateTimeOffset instante)
    {
        var novaTransportadora = Guardas.Texto(transportadora, TamanhoMaximoTransportadora);
        var novoCodigo = Guardas.Texto(codigoRastreio, TamanhoMaximoCodigoRastreio);
        AtualizadoEm = Guardas.Utc(instante);
        DespachadoEm = instante;
        Transportadora = novaTransportadora;
        CodigoRastreio = novoCodigo;
    }
}
