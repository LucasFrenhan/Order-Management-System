using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>
/// Operações de registro e autenticação de usuários (implementadas no REQ-03).
/// </summary>
public interface IServicoAutenticacao
{
    /// <summary>
    /// Registra o usuário com o perfil Cliente. E-mail já cadastrado: <see cref="TipoFalha.Conflito"/>
    /// com <c>USUARIO_EMAIL_JA_CADASTRADO</c>.
    /// </summary>
    Task<Resultado<UsuarioResponseDTO>> RegistrarAsync(RegistrarUsuarioRequestDTO usuario, CancellationToken cancellationToken);

    /// <summary>
    /// Autentica e emite o token. Credenciais inválidas: <see cref="TipoFalha.NaoAutenticado"/> com
    /// mensagem genérica que não revela se o e-mail existe.
    /// </summary>
    Task<Resultado<LoginResponseDTO>> AutenticarAsync(LoginRequestDTO credenciais, CancellationToken cancellationToken);
}
