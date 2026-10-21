using System.ComponentModel.DataAnnotations;

namespace PedidoFulfillment.Application.Contratos.Autenticacao;

/// <summary>
/// Dados de registro de um novo usuário (FR-012). O perfil atribuído é sempre Cliente.
/// </summary>
public sealed record RegistrarUsuarioRequestDTO : IValidatableObject
{
    [Required(ErrorMessage = "O nome é obrigatório.")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "O nome deve ter entre 2 e 100 caracteres.")]
    public string Nome { get; init; } = "";

    [Required(ErrorMessage = "O e-mail é obrigatório.")]
    [EmailAddress(ErrorMessage = "O e-mail deve ter um formato válido.")]
    [MaxLength(256, ErrorMessage = "O e-mail deve ter até 256 caracteres.")]
    public string Email { get; init; } = "";

    [Required(ErrorMessage = "A senha é obrigatória.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "A senha deve ter entre 8 e 128 caracteres.")]
    [DataType(DataType.Password)]
    public string Senha { get; init; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // O DTO não altera o valor; o aparo definitivo é do ServicoAutenticacao.
        var nome = Nome.Trim();
        if (nome.Length is < 2 or > 100)
        {
            yield return new ValidationResult("O nome deve ter entre 2 e 100 caracteres, sem contar espaços nas pontas.", [nameof(Nome)]);
        }

        if (!Senha.Any(char.IsUpper) || !Senha.Any(char.IsLower) || !Senha.Any(char.IsDigit))
        {
            yield return new ValidationResult(
                "A senha deve ter ao menos uma letra maiúscula, uma letra minúscula e um dígito.", [nameof(Senha)]);
        }
    }
}
