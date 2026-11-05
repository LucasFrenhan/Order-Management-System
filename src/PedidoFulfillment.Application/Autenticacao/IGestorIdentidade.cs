using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>
/// Contas e credenciais guardadas pelo provedor de identidade (research R-01). Recebe valores
/// já aparados pelo <c>ServicoAutenticacao</c>.
/// </summary>
public interface IGestorIdentidade
{
    /// <summary>
    /// Cria a conta com o perfil Cliente. E-mail já cadastrado, inclusive por um registro
    /// simultâneo: <see cref="TipoFalha.Conflito"/> com <c>USUARIO_EMAIL_JA_CADASTRADO</c>.
    /// </summary>
    Task<Resultado<UsuarioResponseDTO>> CriarAsync(string nome, string email, string senha, CancellationToken cancellationToken);

    /// <summary>
    /// Verifica e-mail e senha, contando os erros seguidos e aplicando o bloqueio (FR-009).
    /// </summary>
    Task<ResultadoCredenciais> VerificarCredenciaisAsync(string email, string senha, CancellationToken cancellationToken);

    /// <summary>Os perfis atuais da conta, para a renovação (REQ-24, FR-007).</summary>
    Task<IReadOnlyList<PerfilUsuario>> ObterPerfisAsync(Guid usuarioId, CancellationToken cancellationToken);
}
