using System.Diagnostics;
using OpenTelemetry.Trace;

namespace PedidoFulfillment.Infrastructure.Observabilidade;

/// <summary>
/// Amostragem dos traces (REQ-16, R-09 e R-11): consulta ao banco sem span pai é descartada,
/// para o polling (o despachante a cada 1 s, os locks das rotinas) não virar um trace por
/// consulta; o resto segue o pai, ou a taxa quando é raiz. A flag viaja no <c>traceparent</c>,
/// então o pedido inteiro é amostrado ou não.
/// </summary>
internal sealed class AmostradorSemSqlRaiz : Sampler
{
    // A instrumentação do SqlClient passa estes atributos na criação do span (convenção 1.44 e anteriores).
    private static readonly string[] AtributosBanco = ["db.system.name", "db.system"];

    private readonly Sampler padrao;

    public AmostradorSemSqlRaiz(double taxa)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(taxa, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(taxa, 1);
        padrao = new ParentBasedSampler(new TraceIdRatioBasedSampler(taxa));
        Description = $"AmostradorSemSqlRaiz{{{taxa}}}";
    }

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        if (samplingParameters.ParentContext.TraceId == default
            && samplingParameters.Kind == ActivityKind.Client
            && EhConsultaBanco(samplingParameters.Tags))
        {
            return new SamplingResult(SamplingDecision.Drop);
        }

        return padrao.ShouldSample(samplingParameters);
    }

    private static bool EhConsultaBanco(IEnumerable<KeyValuePair<string, object?>>? atributos) =>
        atributos?.Any(atributo => AtributosBanco.Contains(atributo.Key, StringComparer.Ordinal)) == true;
}
