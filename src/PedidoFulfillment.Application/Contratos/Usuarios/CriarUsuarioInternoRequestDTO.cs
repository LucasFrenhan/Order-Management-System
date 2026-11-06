using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Contratos.Usuarios;

/// <summary>
/// Dados da conta interna criada pelo Admin (REQ-25, FR-007). Nome e e-mail seguem as regras do
/// registro. Não há senha: a pessoa a define pelo convite (FR-008). O perfil chega como texto, para
/// que um valor desconhecido dê uma mensagem própria.
/// </summary>
public sealed record CriarUsuarioInternoRequestDTO : IValidatableObject
{
    public const string MensagemPerfil = "O perfil deve ser Admin ou OperadorLogistica.";

    [Required(ErrorMessage = "O nome é obrigatório.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "O nome deve ter entre 2 e 100 caracteres.")]
    public string Nome { get; init; } = "";

    [Required(ErrorMessage = "O e-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "O e-mail deve ter um formato válido.")]
    [MaxLength(256, ErrorMessage = "O e-mail deve ter até 256 caracteres.")]
    public string Email { get; init; } = "";

    [Required(ErrorMessage = "O perfil é obrigatório.")]
    [AllowedValues(nameof(PerfilUsuario.Admin), nameof(PerfilUsuario.OperadorLogistica), ErrorMessage = MensagemPerfil)]
    public string Perfil { get; init; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // O DTO não altera o valor; o aparo definitivo é do ServicoGestaoUsuarios.
        if (Nome.Trim().Length is < 2 or > 100)
        {
            yield return new ValidationResult("O nome deve ter entre 2 e 100 caracteres, sem contar espaços nas pontas.", [nameof(Nome)]);
        }
    }

    /// <summary>O perfil pedido; só depois da validação.</summary>
    public PerfilUsuario PerfilPedido() => Enum.Parse<PerfilUsuario>(Perfil);
}
