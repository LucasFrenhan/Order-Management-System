using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Application.Autenticacao;

namespace PedidoFulfillment.Application.Contratos.Autenticacao;

/// <summary>
/// O token recebido por e-mail e a senha nova (REQ-24, FR-014). Só o token: ele identifica a conta
/// sozinho, e o link não traz dados pessoais.
/// </summary>
public sealed record RedefinicaoSenhaRequestDTO : IValidatableObject
{
    [Required(ErrorMessage = "O token é obrigatório.")]
    [MaxLength(GeradorTokenOpaco.TamanhoMaximoRecebido, ErrorMessage = "O token deve ter até 128 caracteres.")]
    public string Token { get; init; } = "";

    [Required(ErrorMessage = "A nova senha é obrigatória.")]
    [StringLength(RegrasSenha.TamanhoMaximo, MinimumLength = RegrasSenha.TamanhoMinimo, ErrorMessage = RegrasSenha.MensagemTamanho)]
    [DataType(DataType.Password)]
    public string NovaSenha { get; init; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!RegrasSenha.AtendeComposicao(NovaSenha))
        {
            yield return new ValidationResult(RegrasSenha.MensagemComposicao, [nameof(NovaSenha)]);
        }
    }
}
