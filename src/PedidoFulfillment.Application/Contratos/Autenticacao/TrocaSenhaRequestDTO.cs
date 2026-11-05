using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Application.Autenticacao;

namespace PedidoFulfillment.Application.Contratos.Autenticacao;

/// <summary>A senha atual e a senha nova da pessoa conectada (REQ-24, FR-010).</summary>
public sealed record TrocaSenhaRequestDTO : IValidatableObject
{
    [Required(ErrorMessage = "A senha atual é obrigatória.")]
    [DataType(DataType.Password)]
    public string SenhaAtual { get; init; } = "";

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
