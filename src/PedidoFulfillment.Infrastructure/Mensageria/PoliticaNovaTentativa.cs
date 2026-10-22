using System.Globalization;
using System.Text;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Nova tentativa ou DLQ (REQ-06, FR-012, FR-013; clarify: primeira entrega + 3 novas
/// tentativas). O número de tentativas feitas viaja no cabeçalho <c>x-tentativas</c>.
/// </summary>
internal static class PoliticaNovaTentativa
{
    public const string CabecalhoTentativas = "x-tentativas";
    public const string CabecalhoUltimoErro = "x-ultimo-erro";
    public const string CabecalhoMotivo = "x-motivo";
    public const string CabecalhoFalhouEm = "x-falhou-em";

    /// <summary>Espera em ms antes da próxima tentativa, ou nulo quando é hora de ir para a DLQ.</summary>
    public static int? ProximaEspera(int tentativasFeitas, IReadOnlyList<int> intervalosMs) =>
        tentativasFeitas < intervalosMs.Count ? intervalosMs[tentativasFeitas] : null;

    /// <summary>Lê o cabeçalho; ausente ou inválido conta como 0.</summary>
    public static int TentativasFeitas(IDictionary<string, object?>? cabecalhos)
    {
        if (cabecalhos is null || !cabecalhos.TryGetValue(CabecalhoTentativas, out var valor))
        {
            return 0;
        }

        var tentativas = valor switch
        {
            int inteiro => inteiro,
            long longo => (int)longo,
            byte[] texto when int.TryParse(Encoding.UTF8.GetString(texto), NumberStyles.Integer, CultureInfo.InvariantCulture, out var lido) => lido,
            _ => 0
        };
        return Math.Max(tentativas, 0);
    }
}
