using System.ComponentModel.DataAnnotations;
using System.Globalization;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Contratos.Usuarios;

/// <summary>
/// Parâmetros de <c>GET /api/v1/admin/usuarios</c> (REQ-25), como texto, para o 400 trazer todos os
/// erros de uma vez, como as demais listagens. O cursor é conferido no serviço.
/// </summary>
public sealed record ListarUsuariosRequestDTO : IValidatableObject
{
    public const int TamanhoPadrao = 20;
    public const int TamanhoMaximo = 100;
    public const int TamanhoMinimoBusca = 2;
    public const int TamanhoMaximoBusca = 100;
    public const string MensagemPerfilDesconhecido = "O perfil deve ser Admin, OperadorLogistica ou Cliente.";

    public string? Perfil { get; init; }

    public string? Situacao { get; init; }

    public string? Busca { get; init; }

    public string? Tamanho { get; init; }

    public string? Cursor { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Perfil is not null && !TentarPerfil(Perfil, out _))
        {
            yield return new ValidationResult(MensagemPerfilDesconhecido, ["perfil"]);
        }

        if (Situacao is not null && !Enum.GetNames<SituacaoUsuario>().Contains(Situacao))
        {
            yield return new ValidationResult("A situação deve ser Ativa ou Desativada.", ["situacao"]);
        }

        if (Busca is not null && Busca.Trim().Length is < TamanhoMinimoBusca or > TamanhoMaximoBusca)
        {
            yield return new ValidationResult($"A busca deve ter entre {TamanhoMinimoBusca} e {TamanhoMaximoBusca} caracteres, sem contar espaços nas pontas.", ["busca"]);
        }

        if (Tamanho is not null && !TentarTamanho(Tamanho, out _))
        {
            yield return new ValidationResult($"O tamanho deve ser um número entre 1 e {TamanhoMaximo}.", ["tamanho"]);
        }
    }

    /// <summary>Só os nomes do contrato: nem número, nem outra caixa.</summary>
    public static bool TentarPerfil(string texto, out PerfilUsuario perfil)
    {
        perfil = default;
        return Enum.GetNames<PerfilUsuario>().Contains(texto) && Enum.TryParse(texto, out perfil);
    }

    /// <summary>Só depois da validação.</summary>
    public PerfilUsuario? PerfilPedido() => Perfil is null ? null : Enum.Parse<PerfilUsuario>(Perfil);

    /// <summary>Só depois da validação.</summary>
    public SituacaoUsuario? SituacaoPedida() => Situacao is null ? null : Enum.Parse<SituacaoUsuario>(Situacao);

    /// <summary>A busca sem os espaços das pontas; nula sem busca.</summary>
    public string? BuscaAparada() => Busca?.Trim();

    /// <summary>Tamanho da página; o padrão quando ausente. Só depois da validação.</summary>
    public int TamanhoPagina() => Tamanho is null ? TamanhoPadrao : int.Parse(Tamanho, NumberStyles.None, CultureInfo.InvariantCulture);

    private static bool TentarTamanho(string texto, out int tamanho) =>
        int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out tamanho) && tamanho is >= 1 and <= TamanhoMaximo;
}
