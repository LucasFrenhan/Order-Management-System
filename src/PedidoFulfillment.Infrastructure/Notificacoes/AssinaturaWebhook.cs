using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PedidoFulfillment.Infrastructure.Notificacoes;

/// <summary>
/// Cabeçalhos e assinatura do Standard Webhooks v1 (REQ-18, FR-013, research R-06;
/// <c>contracts/webhooks.md</c>): HMAC-SHA256 de <c>{id}.{timestamp}.{corpo}</c> com os bytes do
/// segredo, em Base64, com o prefixo <c>v1,</c>.
/// </summary>
internal static class AssinaturaWebhook
{
    public const string CabecalhoId = "webhook-id";
    public const string CabecalhoTimestamp = "webhook-timestamp";
    public const string CabecalhoAssinatura = "webhook-signature";

    public static string Timestamp(DateTimeOffset instante) => instante.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    public static string Assinar(byte[] segredo, string id, string timestamp, string corpo)
    {
        var conteudo = Encoding.UTF8.GetBytes($"{id}.{timestamp}.{corpo}");
        return "v1," + Convert.ToBase64String(HMACSHA256.HashData(segredo, conteudo));
    }
}
