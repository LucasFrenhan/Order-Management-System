namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// Um token de renovação, só pelo hash (REQ-24, FR-003). Os usados ficam até a sessão acabar: são
/// eles que detectam a reutilização (R-02).
/// </summary>
internal sealed class TokenRenovacao
{
    public const int TamanhoHash = 32;

    public required byte[] HashToken { get; init; }

    public Guid SessaoId { get; init; }

    public DateTimeOffset ExpiraEm { get; init; }

    public DateTimeOffset? UsadoEm { get; init; }
}
