using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>
/// Emite o token de acesso do login (contracts/token-acesso.md).
/// </summary>
public interface IEmissorToken
{
    LoginResponseDTO Emitir(Guid usuarioId, IReadOnlyList<PerfilUsuario> perfis);
}
