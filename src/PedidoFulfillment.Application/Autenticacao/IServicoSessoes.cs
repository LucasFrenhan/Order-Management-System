using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>Início, renovação e encerramento de sessões (REQ-24).</summary>
public interface IServicoSessoes
{
    /// <summary>Cria a sessão do login e emite o par de tokens (FR-001).</summary>
    Task<LoginResponseDTO> IniciarAsync(Guid usuarioId, IReadOnlyList<PerfilUsuario> perfis, CancellationToken cancellationToken);

    /// <summary>
    /// Troca o token por um par novo, com os perfis atuais (FR-002, FR-007). Inválido, expirado,
    /// reutilizado ou de sessão encerrada: a mesma <see cref="TipoFalha.NaoAutenticado"/> (FR-006).
    /// </summary>
    Task<Resultado<LoginResponseDTO>> RenovarAsync(RenovacaoRequestDTO renovacao, CancellationToken cancellationToken);

    /// <summary>Encerra a sessão do token. Revogado, expirado ou inexistente termina igual, sem erro (FR-008).</summary>
    Task EncerrarAsync(LogoutRequestDTO logout, CancellationToken cancellationToken);
}
