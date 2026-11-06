using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Usuarios;

/// <summary>
/// Contas e perfis guardados pelo provedor de identidade, para a gestão pelo Admin (REQ-25). As
/// alterações rodam uma de cada vez, entre todas as instâncias, e conferem de novo quem chama (R-03).
/// </summary>
public interface IGestorUsuarios
{
    /// <summary>Tem o perfil Admin, está ativa e já definiu a senha (R-02).</summary>
    Task<bool> EhAdminAtivoAsync(Guid usuarioId, CancellationToken cancellationToken);

    Task<ContaGestao?> ObterAsync(Guid usuarioId, CancellationToken cancellationToken);

    /// <summary>Na ordem de <c>(Nome, Id)</c>. <paramref name="quantidade"/> inclui a linha extra que indica se há mais.</summary>
    Task<IReadOnlyList<ContaGestao>> ListarAsync(
        FiltroListagemUsuarios filtro, PosicaoCursorUsuarios? posicao, int quantidade, CancellationToken cancellationToken);

    /// <summary>
    /// Cria a conta sem senha, com o perfil e o pedido do convite na mesma transação (R-09). Quem deixou
    /// de ser Admin ativo: sem permissão. E-mail já cadastrado: <c>USUARIO_EMAIL_JA_CADASTRADO</c>.
    /// </summary>
    Task<Resultado<ContaGestao>> CriarAsync(Guid atorId, string nome, string email, PerfilUsuario perfil, CancellationToken cancellationToken);

    /// <summary>Avalia as regras sobre o estado lido dentro do lock e, se for o caso, aplica a alteração (R-04).</summary>
    Task<ResultadoAlteracaoUsuario> AlterarAsync(Guid atorId, Guid alvoId, AlteracaoUsuario alteracao, CancellationToken cancellationToken);
}
