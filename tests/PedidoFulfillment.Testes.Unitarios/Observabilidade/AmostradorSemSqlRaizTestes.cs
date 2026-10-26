using System.Diagnostics;
using FluentAssertions;
using OpenTelemetry.Trace;
using PedidoFulfillment.Infrastructure.Observabilidade;

namespace PedidoFulfillment.Testes.Unitarios.Observabilidade;

/// <summary>Amostragem dos traces (REQ-16, R-09 e R-11; contrato › Amostragem).</summary>
public sealed class AmostradorSemSqlRaizTestes
{
    private static readonly KeyValuePair<string, object?>[] TagsSql = [new("db.system.name", "microsoft.sql_server")];

    private static readonly ActivityContext PaiAmostrado =
        new(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded, isRemote: true);

    private static readonly ActivityContext PaiNaoAmostrado =
        new(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.None, isRemote: true);

    [Fact]
    public void Consulta_ao_banco_sem_pai_e_descartada()
    {
        Decidir(1.0, default, ActivityKind.Client, TagsSql).Should().Be(SamplingDecision.Drop);
    }

    [Fact]
    public void Consulta_ao_banco_com_pai_amostrado_e_registrada()
    {
        Decidir(1.0, PaiAmostrado, ActivityKind.Client, TagsSql).Should().Be(SamplingDecision.RecordAndSample);
    }

    [Fact]
    public void Consulta_ao_banco_com_pai_nao_amostrado_e_descartada()
    {
        Decidir(1.0, PaiNaoAmostrado, ActivityKind.Client, TagsSql).Should().Be(SamplingDecision.Drop);
    }

    [Theory]
    [InlineData(1.0, SamplingDecision.RecordAndSample)]
    [InlineData(0.0, SamplingDecision.Drop)]
    public void Raiz_que_nao_e_consulta_segue_a_taxa(double taxa, SamplingDecision esperada)
    {
        Decidir(taxa, default, ActivityKind.Server, null).Should().Be(esperada);
    }

    [Fact]
    public void Filho_segue_o_pai_mesmo_com_taxa_zero()
    {
        Decidir(0.0, PaiAmostrado, ActivityKind.Consumer, null).Should().Be(SamplingDecision.RecordAndSample);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void Taxa_fora_da_faixa_e_recusada(double taxa)
    {
        var criar = () => new AmostradorSemSqlRaiz(taxa);

        criar.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static SamplingDecision Decidir(double taxa, ActivityContext pai, ActivityKind tipo, IEnumerable<KeyValuePair<string, object?>>? tags)
    {
        var traceId = pai.TraceId == default ? ActivityTraceId.CreateRandom() : pai.TraceId;
        return new AmostradorSemSqlRaiz(taxa)
            .ShouldSample(new SamplingParameters(pai, traceId, "teste", tipo, tags))
            .Decision;
    }
}
