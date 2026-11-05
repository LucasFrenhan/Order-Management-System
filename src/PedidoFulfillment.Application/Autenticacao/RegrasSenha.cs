namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>
/// Regras de senha do contrato v1, usadas no registro, na troca e na redefinição (REQ-24, R-09). O
/// tamanho fica no atributo de cada DTO, com estas constantes.
/// </summary>
public static class RegrasSenha
{
    public const int TamanhoMinimo = 8;
    public const int TamanhoMaximo = 128;
    public const string MensagemTamanho = "A senha deve ter entre 8 e 128 caracteres.";
    public const string MensagemComposicao = "A senha deve ter ao menos uma letra maiúscula, uma letra minúscula e um dígito.";

    public static bool AtendeComposicao(string senha) =>
        senha.Any(char.IsUpper) && senha.Any(char.IsLower) && senha.Any(char.IsDigit);
}
