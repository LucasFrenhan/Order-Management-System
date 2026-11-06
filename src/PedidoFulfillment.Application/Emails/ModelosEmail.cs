namespace PedidoFulfillment.Application.Emails;

/// <summary>
/// Os textos dos e-mails (REQ-24). A API não tem telas: o e-mail traz o token e, com um endereço de
/// aplicação configurado, também um link para ela.
/// </summary>
public static class ModelosEmail
{
    private const string CaminhoRedefinicao = "redefinir-senha";
    private const string CaminhoConfirmacao = "confirmar-email";

    public static MensagemEmail Recuperacao(string destino, string token, TimeSpan validade, string? urlBase) => new(
        destino,
        "Recuperação de senha",
        $"""
        Recebemos um pedido para redefinir a senha desta conta.

        {Instrucao(token, urlBase, CaminhoRedefinicao)}

        O token vale por {Descrever(validade)} e serve uma só vez.

        Se não foi você, ignore este e-mail: a senha continua a mesma.
        """);

    public static MensagemEmail Confirmacao(string destino, string token, TimeSpan validade, string? urlBase) => new(
        destino,
        "Confirmação de e-mail",
        $"""
        Confirme que este endereço de e-mail é seu.

        {Instrucao(token, urlBase, CaminhoConfirmacao)}

        O token vale por {Descrever(validade)} e serve uma só vez.

        Se não foi você, ignore este e-mail.
        """);

    /// <summary>Sem a senha, que ninguém informou, e sem o nome de quem criou a conta (REQ-25, contrato).</summary>
    public static MensagemEmail Convite(string destino, string token, TimeSpan validade, string? urlBase) => new(
        destino,
        "Convite para acessar o sistema",
        $"""
        Uma conta foi criada para você. Defina a sua senha para começar a usar.

        {Instrucao(token, urlBase, CaminhoRedefinicao)}

        O token vale por {Descrever(validade)} e serve uma só vez. Depois desse prazo, use a recuperação de senha com este e-mail.

        Se não esperava este convite, ignore este e-mail.
        """);

    public static MensagemEmail AvisoSenhaRedefinida(string destino) => new(
        destino,
        "Sua senha foi redefinida",
        """
        A senha desta conta acabou de ser redefinida, e todas as sessões foram encerradas.

        Se não foi você, peça uma nova recuperação de senha agora.
        """);

    private static string Instrucao(string token, string? urlBase, string caminho) =>
        string.IsNullOrWhiteSpace(urlBase)
            ? $"Token: {token}"
            : $"Acesse: {urlBase.TrimEnd('/')}/{caminho}?token={Uri.EscapeDataString(token)}\n\nToken: {token}";

    private static string Descrever(TimeSpan validade) =>
        validade.TotalMinutes < 60 ? $"{(int)validade.TotalMinutes} minutos" : $"{(int)validade.TotalHours} horas";
}
