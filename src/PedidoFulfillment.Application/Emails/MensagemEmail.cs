namespace PedidoFulfillment.Application.Emails;

/// <summary>
/// Uma mensagem pronta para enviar. O corpo pode trazer um token: <see cref="ToString"/> não mostra
/// os campos, para um log acidental não vazá-lo (SC-008).
/// </summary>
public sealed record MensagemEmail(string Destino, string Assunto, string Corpo)
{
    public override string ToString() => nameof(MensagemEmail);
}
