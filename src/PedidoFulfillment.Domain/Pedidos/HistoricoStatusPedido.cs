using PedidoFulfillment.Domain.Comum;

namespace PedidoFulfillment.Domain.Pedidos;

/// <summary>
/// Uma mudança de status do pedido (FR-022). Só de inclusão: não tem métodos de alteração.
/// O <see cref="Id"/> sequencial define a ordem das entradas.
/// </summary>
public sealed class HistoricoStatusPedido
{
    public const int TamanhoMaximoMotivo = 500;

    internal HistoricoStatusPedido(Guid pedidoId, StatusPedido? statusAnterior, StatusPedido statusNovo, Guid? autorId, string? motivo, DateTimeOffset ocorridoEm)
    {
        PedidoId = pedidoId;
        StatusAnterior = statusAnterior;
        StatusNovo = statusNovo;
        AutorId = autorId;
        Motivo = Guardas.TextoOpcional(motivo, TamanhoMaximoMotivo);
        OcorridoEm = Guardas.Utc(ocorridoEm);
    }

    private HistoricoStatusPedido()
    {
    }

    public long Id { get; private set; }

    public Guid PedidoId { get; private set; }

    /// <summary>Nulo só na entrada de criação do pedido.</summary>
    public StatusPedido? StatusAnterior { get; private set; }

    public StatusPedido StatusNovo { get; private set; }

    public DateTimeOffset OcorridoEm { get; private set; }

    /// <summary>Usuário que fez a mudança; nulo quando foi um worker.</summary>
    public Guid? AutorId { get; private set; }

    public string? Motivo { get; private set; }
}
