using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Usuarios;

/// <summary>
/// Regras das alterações de usuário, sem acesso a dados (REQ-25, R-04). A ordem importa: o último
/// Admin vem antes da própria conta, para o único Admin receber o código do último Admin (FR-003).
/// </summary>
public static class RegrasGestaoUsuarios
{
    public static readonly Falha SemPermissao = new(
        TipoFalha.SemPermissao, "Acesso negado.", "O usuário não tem permissão para esta operação.");

    public static readonly Falha NaoEncontrado = new(
        TipoFalha.NaoEncontrado, "Usuário não encontrado.", "Não existe usuário com este identificador.");

    public static readonly Falha UltimoAdmin = new(
        TipoFalha.Conflito, "Último Admin.", "O sistema precisa de ao menos um Admin ativo.", CodigosErro.UsuarioUltimoAdmin);

    public static readonly Falha PropriaConta = new(
        TipoFalha.Conflito, "Operação sobre a própria conta.", "Peça a outro Admin para fazer esta alteração.", CodigosErro.UsuarioPropriaConta);

    public static readonly Falha UltimoPerfil = new(
        TipoFalha.Conflito, "Último perfil da conta.", "A conta precisa manter ao menos um perfil.", CodigosErro.UsuarioUltimoPerfil);

    public static readonly Falha PerfisIncompativeis = new(
        TipoFalha.Conflito, "Perfis incompatíveis.", "O perfil Cliente não se combina com perfis internos.", CodigosErro.UsuarioPerfisIncompativeis);

    private static readonly DecisaoAlteracao Aplicar = new(DesfechoAlteracao.Aplicar);
    private static readonly DecisaoAlteracao SemEfeito = new(DesfechoAlteracao.SemEfeito);

    public static DecisaoAlteracao Avaliar(CenarioAlteracao cenario, AlteracaoUsuario alteracao)
    {
        if (!cenario.AtorEhAdminAtivo)
        {
            return Recusar(SemPermissao);
        }

        if (cenario.Alvo is not { } alvo)
        {
            return Recusar(NaoEncontrado);
        }

        if (JaEstaNoEstadoPedido(alvo, alteracao))
        {
            return SemEfeito;
        }

        if (TiraAdmin(alteracao))
        {
            if (alvo.EhAdminAtivo && cenario.AdminsAtivos <= 1)
            {
                return Recusar(UltimoAdmin);
            }

            if (alvo.Id == cenario.AtorId)
            {
                return Recusar(PropriaConta);
            }
        }

        if (alteracao.Tipo == TipoAlteracaoUsuario.RetirarPerfil && alvo.Perfis.Count == 1)
        {
            return Recusar(UltimoPerfil);
        }

        return alteracao is { Tipo: TipoAlteracaoUsuario.AtribuirPerfil, Perfil: { } perfil } && MisturariaGrupos(alvo, perfil)
            ? Recusar(PerfisIncompativeis)
            : Aplicar;
    }

    /// <summary>As recusas que entram na auditoria (FR-021).</summary>
    public static bool RecusaAuditada(Falha falha) =>
        falha.Codigo is CodigosErro.UsuarioUltimoAdmin or CodigosErro.UsuarioPropriaConta;

    private static bool JaEstaNoEstadoPedido(ContaGestao alvo, AlteracaoUsuario alteracao) => alteracao switch
    {
        { Tipo: TipoAlteracaoUsuario.AtribuirPerfil, Perfil: { } perfil } => alvo.Perfis.Contains(perfil),
        { Tipo: TipoAlteracaoUsuario.RetirarPerfil, Perfil: { } perfil } => !alvo.Perfis.Contains(perfil),
        { Tipo: TipoAlteracaoUsuario.Desativar } => alvo.Desativada,
        { Tipo: TipoAlteracaoUsuario.Reativar } => !alvo.Desativada,
        _ => throw new ArgumentOutOfRangeException(nameof(alteracao), "Atribuição e retirada exigem o perfil.")
    };

    private static bool TiraAdmin(AlteracaoUsuario alteracao) => alteracao is
    { Tipo: TipoAlteracaoUsuario.Desativar } or
    { Tipo: TipoAlteracaoUsuario.RetirarPerfil, Perfil: PerfilUsuario.Admin };

    /// <summary>Conta sem perfil aceita qualquer grupo; dados anteriores já misturados não aceitam outro perfil.</summary>
    private static bool MisturariaGrupos(ContaGestao alvo, PerfilUsuario perfil) =>
        perfil == PerfilUsuario.Cliente
            ? alvo.Perfis.Any(p => p != PerfilUsuario.Cliente)
            : alvo.Perfis.Contains(PerfilUsuario.Cliente);

    private static DecisaoAlteracao Recusar(Falha falha) => new(DesfechoAlteracao.Recusar, falha);
}
