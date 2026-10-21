namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>
/// Forma do e-mail que pode ir para o log (FR-019): primeira letra da parte local, <c>***</c>
/// e o domínio. Qualquer valor fora desse formato vira só <c>***</c>.
/// </summary>
public static class MascaraEmail
{
    public static string Mascarar(string? email)
    {
        var arroba = email?.IndexOf('@') ?? -1;
        if (email is null || arroba <= 0 || arroba == email.Length - 1 || arroba != email.LastIndexOf('@'))
        {
            return "***";
        }

        return $"{email[0]}***{email[arroba..]}";
    }
}
