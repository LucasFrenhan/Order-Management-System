using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Usuarios;

/// <summary>Uma conta como a gestão a enxerga, sem nenhum dado de credencial (REQ-25, FR-006).</summary>
public sealed record ContaGestao(
    Guid Id,
    string Nome,
    string Email,
    IReadOnlyList<PerfilUsuario> Perfis,
    bool Desativada,
    bool EmailConfirmado,
    bool ConvitePendente)
{
    /// <summary>O que a regra do último Admin conta. O bloqueio por erros de senha é temporário e não entra (data-model).</summary>
    public bool EhAdminAtivo => Perfis.Contains(PerfilUsuario.Admin) && !Desativada && !ConvitePendente;
}

public enum TipoAlteracaoUsuario
{
    AtribuirPerfil,
    RetirarPerfil,
    Desativar,
    Reativar
}

/// <param name="Perfil">Em <see cref="TipoAlteracaoUsuario.AtribuirPerfil"/> e <see cref="TipoAlteracaoUsuario.RetirarPerfil"/>.</param>
public sealed record AlteracaoUsuario(TipoAlteracaoUsuario Tipo, PerfilUsuario? Perfil = null);

/// <summary>O estado lido dentro do lock, sobre o qual as regras decidem (R-03, R-04).</summary>
/// <param name="Alvo">Nula se a conta não existe.</param>
public sealed record CenarioAlteracao(Guid AtorId, bool AtorEhAdminAtivo, ContaGestao? Alvo, int AdminsAtivos);

public enum DesfechoAlteracao
{
    Aplicar,

    /// <summary>A conta já está no estado pedido: sucesso, sem alterar nem auditar (FR-012, FR-015).</summary>
    SemEfeito,

    Recusar
}

/// <param name="Falha">Em <see cref="DesfechoAlteracao.Recusar"/>.</param>
public sealed record DecisaoAlteracao(DesfechoAlteracao Desfecho, Falha? Falha = null);

/// <summary>Como terminou uma alteração. <paramref name="Conta"/> é o estado depois dela, salvo na recusa.</summary>
public sealed record ResultadoAlteracaoUsuario(DecisaoAlteracao Decisao, ContaGestao? Conta);
