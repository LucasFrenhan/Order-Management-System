using System.Security.Cryptography;

namespace PedidoFulfillment.Application.Notificacoes;

/// <summary>
/// Segredo de assinatura de um destino (REQ-18, FR-009, research R-15): 32 bytes aleatórios,
/// mostrados uma vez como <c>whsec_</c> + Base64, no formato do Standard Webhooks.
/// </summary>
public static class SegredoWebhook
{
    public const string Prefixo = "whsec_";
    public const int Tamanho = 32;

    public static byte[] Gerar() => RandomNumberGenerator.GetBytes(Tamanho);

    public static string Texto(byte[] segredo) => Prefixo + Convert.ToBase64String(segredo);

    /// <summary>Os bytes de um segredo em texto, com ou sem o prefixo.</summary>
    public static byte[] Bytes(string texto) =>
        Convert.FromBase64String(texto.StartsWith(Prefixo, StringComparison.Ordinal) ? texto[Prefixo.Length..] : texto);
}
