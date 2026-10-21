using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>
/// Desfecho da verificação de credenciais (data-model.md). Só <see cref="Valida"/> leva a um
/// token; as demais situações viram o mesmo 401 genérico e diferem apenas no log.
/// </summary>
public enum SituacaoCredenciais
{
    Valida,
    Invalida,
    Bloqueada,
    BloqueadaAgora
}

/// <summary>
/// Resultado de <see cref="IGestorIdentidade.VerificarCredenciaisAsync"/>. <see cref="UsuarioId"/>
/// é nulo quando o e-mail não tem conta.
/// </summary>
public sealed record ResultadoCredenciais(SituacaoCredenciais Situacao, Guid? UsuarioId, IReadOnlyList<PerfilUsuario> Perfis)
{
    public static ResultadoCredenciais Valida(Guid usuarioId, IReadOnlyList<PerfilUsuario> perfis) =>
        new(SituacaoCredenciais.Valida, usuarioId, perfis);

    public static ResultadoCredenciais Invalida(Guid? usuarioId) => new(SituacaoCredenciais.Invalida, usuarioId, []);

    public static ResultadoCredenciais Bloqueada(Guid usuarioId) => new(SituacaoCredenciais.Bloqueada, usuarioId, []);

    public static ResultadoCredenciais BloqueadaAgora(Guid usuarioId) => new(SituacaoCredenciais.BloqueadaAgora, usuarioId, []);
}
