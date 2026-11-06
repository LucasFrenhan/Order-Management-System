using PedidoFulfillment.Application.Contratos.Usuarios;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Usuarios;

/// <summary>Filtros da listagem de usuários já convertidos (REQ-25, R-07); combinam por "e".</summary>
public sealed record FiltroListagemUsuarios
{
    public PerfilUsuario? Perfil { get; init; }

    public SituacaoUsuario? Situacao { get; init; }

    /// <summary>Trecho do nome ou do e-mail, sem os espaços das pontas; nulo = sem busca.</summary>
    public string? Busca { get; init; }

    /// <summary>Texto único para os mesmos filtros; entra no <c>mac</c> do cursor. A busca fica por último porque aceita qualquer caractere.</summary>
    public string TextoCanonico() => $"{Perfil}|{Situacao}|{Busca?.ToUpperInvariant()}";
}
