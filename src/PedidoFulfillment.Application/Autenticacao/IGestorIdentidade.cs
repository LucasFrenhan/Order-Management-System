using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>Como terminou a troca de senha (REQ-24). Conta bloqueada é <see cref="SenhaAtualIncorreta"/>: o bloqueio não aparece.</summary>
public enum ResultadoTrocaSenha
{
    Trocada,
    SenhaAtualIncorreta,
    MesmaSenha
}

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

    /// <summary>
    /// Em uma transação: consome o token, troca a senha, zera o bloqueio, confirma o e-mail, encerra as
    /// sessões e pede o e-mail de aviso (FR-011, FR-015). Devolve a conta; token que não vale, nulo.
    /// </summary>
    Task<Guid?> RedefinirSenhaAsync(byte[] hashToken, string novaSenha, CancellationToken cancellationToken);

    /// <summary>
    /// Verifica a senha atual, contando os erros para o bloqueio (FR-012), e, em uma transação, troca a
    /// senha e encerra as sessões (FR-011). Em conta bloqueada, a senha nem é verificada.
    /// </summary>
    Task<ResultadoTrocaSenha> TrocarSenhaAsync(Guid usuarioId, string senhaAtual, string novaSenha, CancellationToken cancellationToken);

    /// <summary>Em uma transação: consome o token e confirma o e-mail. Devolve a conta; token que não vale, nulo.</summary>
    Task<Guid?> ConfirmarEmailAsync(byte[] hashToken, CancellationToken cancellationToken);
}
