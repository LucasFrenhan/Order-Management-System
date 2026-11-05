using PedidoFulfillment.Application.Autenticacao;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>Token de recuperação de senha ou de confirmação de e-mail, só pelo hash (REQ-24, data-model).</summary>
internal sealed class TokenUsoUnico
{
    public required byte[] HashToken { get; init; }

    public Guid UsuarioId { get; init; }

    public FinalidadeTokenUsoUnico Finalidade { get; init; }

    public DateTimeOffset ExpiraEm { get; init; }

    public DateTimeOffset? UsadoEm { get; init; }
}
