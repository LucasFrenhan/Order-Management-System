using PedidoFulfillment.Application.Mensageria;

namespace PedidoFulfillment.Application.Contratos.Administracao;

/// <summary>Um pedido de reenvio da DLQ, com a situação e, depois de executado, as contagens (REQ-14, contrato).</summary>
public sealed record PedidoReenvioDlqResponseDTO
{
    public required Guid Id { get; init; }

    public required string Consumidor { get; init; }

    public required SituacaoReenvioDlq Situacao { get; init; }

    public required Guid AutorId { get; init; }

    public string? TipoEvento { get; init; }

    public DateTimeOffset? FalhouDe { get; init; }

    public DateTimeOffset? FalhouAte { get; init; }

    public string? TrechoMotivo { get; init; }

    public required DateTimeOffset RegistradoEm { get; init; }

    public DateTimeOffset? IniciadoEm { get; init; }

    public DateTimeOffset? ConcluidoEm { get; init; }

    public required int Reenviadas { get; init; }

    public required int BloqueadasPeloLimite { get; init; }

    public required int ForaDoFiltro { get; init; }

    public string? MotivoFalha { get; init; }

    public static PedidoReenvioDlqResponseDTO De(PedidoReenvioDlq pedido) => new()
    {
        Id = pedido.Id,
        Consumidor = pedido.Consumidor,
        Situacao = pedido.Situacao,
        AutorId = pedido.AutorId,
        TipoEvento = pedido.TipoEvento,
        FalhouDe = pedido.FalhouDe,
        FalhouAte = pedido.FalhouAte,
        TrechoMotivo = pedido.TrechoMotivo,
        RegistradoEm = pedido.RegistradoEm,
        IniciadoEm = pedido.IniciadoEm,
        ConcluidoEm = pedido.ConcluidoEm,
        Reenviadas = pedido.Reenviadas,
        BloqueadasPeloLimite = pedido.BloqueadasPeloLimite,
        ForaDoFiltro = pedido.ForaDoFiltro,
        MotivoFalha = pedido.MotivoFalha
    };
}
