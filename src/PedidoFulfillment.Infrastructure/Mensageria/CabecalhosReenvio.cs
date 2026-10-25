using System.Globalization;
using System.Text;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Leitura dos cabeçalhos da DLQ e montagem da cópia reenviada (REQ-14, R-06). Os cabeçalhos
/// são de transporte: o envelope v1 não muda. O RabbitMQ entrega textos como bytes UTF-8.
/// </summary>
internal static class CabecalhosReenvio
{
    public const string Reenvios = "x-reenvios";
    public const string ReenviadoEm = "x-reenviado-em";
    public const string PedidoReenvio = "x-pedido-reenvio";

    /// <summary>Quantas vezes a mensagem já foi reenviada; ausente ou inválido conta como 0.</summary>
    public static int ReenviosFeitos(IDictionary<string, object?>? cabecalhos)
    {
        if (cabecalhos is null || !cabecalhos.TryGetValue(Reenvios, out var valor))
        {
            return 0;
        }

        var reenvios = valor switch
        {
            int inteiro => inteiro,
            long longo => (int)longo,
            byte[] texto when int.TryParse(Encoding.UTF8.GetString(texto), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lido) => lido,
            _ => 0
        };
        return Math.Max(reenvios, 0);
    }

    public static string? Motivo(IDictionary<string, object?>? cabecalhos) => Texto(cabecalhos, PoliticaNovaTentativa.CabecalhoMotivo);

    /// <summary>O instante em que a mensagem foi para a DLQ (ISO 8601, gravado pelo REQ-06), se houver.</summary>
    public static DateTimeOffset? FalhouEm(IDictionary<string, object?>? cabecalhos) =>
        Texto(cabecalhos, PoliticaNovaTentativa.CabecalhoFalhouEm) is { } texto
        && DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var instante)
            ? instante
            : null;

    /// <summary>
    /// Os cabeçalhos da cópia que volta à fila original: sem as tentativas e o último erro (as
    /// tentativas recomeçam do zero), com o contador de reenvios, o instante e o pedido.
    /// </summary>
    public static Dictionary<string, object?> ParaReenvio(IDictionary<string, object?>? originais, Guid pedidoReenvioId, DateTimeOffset instante)
    {
        var cabecalhos = new Dictionary<string, object?>(originais ?? new Dictionary<string, object?>());
        cabecalhos.Remove(PoliticaNovaTentativa.CabecalhoTentativas);
        cabecalhos.Remove(PoliticaNovaTentativa.CabecalhoUltimoErro);
        cabecalhos[Reenvios] = ReenviosFeitos(originais) + 1;
        cabecalhos[ReenviadoEm] = instante.ToString("O", CultureInfo.InvariantCulture);
        cabecalhos[PedidoReenvio] = pedidoReenvioId.ToString();
        return cabecalhos;
    }

    private static string? Texto(IDictionary<string, object?>? cabecalhos, string chave) =>
        cabecalhos is not null && cabecalhos.TryGetValue(chave, out var valor)
            ? valor switch
            {
                byte[] bytes => Encoding.UTF8.GetString(bytes),
                string texto => texto,
                _ => null
            }
            : null;
}
