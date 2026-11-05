namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>Uma linha por login (REQ-24, data-model). Dado técnico de autenticação, como as tabelas do Identity.</summary>
internal sealed class SessaoAutenticacao
{
    public Guid Id { get; init; }

    public Guid UsuarioId { get; init; }

    public DateTimeOffset IniciadaEm { get; init; }

    /// <summary>A rotação não estende (FR-005).</summary>
    public DateTimeOffset ExpiraEm { get; init; }

    /// <summary>Preenchida no logout, na reutilização e na troca ou redefinição de senha.</summary>
    public DateTimeOffset? EncerradaEm { get; init; }
}
