using System.Diagnostics;
using System.Text;
using FluentAssertions;
using PedidoFulfillment.Infrastructure.Observabilidade;

namespace PedidoFulfillment.Testes.Unitarios.Observabilidade;

/// <summary>Leitura e validação do contexto de rastreamento (REQ-16, data-model, FR-013).</summary>
public sealed class ContextoRastreioTestes
{
    private const string Valido = "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01";

    [Fact]
    public void Atual_devolve_o_id_w3c_da_activity_corrente()
    {
        using var activity = new Activity("teste").SetIdFormat(ActivityIdFormat.W3C).Start();

        ContextoRastreio.Atual().Should().Be(activity.Id).And.HaveLength(55);
    }

    [Fact]
    public void Atual_e_nulo_sem_activity()
    {
        var anterior = Activity.Current;
        Activity.Current = null;
        try
        {
            ContextoRastreio.Atual().Should().BeNull();
        }
        finally
        {
            Activity.Current = anterior;
        }
    }

    [Fact]
    public void Atual_e_nulo_com_id_hierarquico()
    {
        using var activity = new Activity("teste").SetIdFormat(ActivityIdFormat.Hierarchical).Start();

        ContextoRastreio.Atual().Should().BeNull();
    }

    [Fact]
    public void Le_um_traceparent_valido()
    {
        ContextoRastreio.TentarLer(Valido, out var contexto).Should().BeTrue();

        contexto.TraceId.ToHexString().Should().Be("0af7651916cd43dd8448eb211c80319c");
        contexto.SpanId.ToHexString().Should().Be("b7ad6b7169203331");
        contexto.TraceFlags.Should().Be(ActivityTraceFlags.Recorded);
        contexto.IsRemote.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-0")]
    [InlineData("00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01-")]
    [InlineData("ff-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01")]
    [InlineData("00-00000000000000000000000000000000-b7ad6b7169203331-01")]
    [InlineData("00-0af7651916cd43dd8448eb211c80319c-0000000000000000-01")]
    [InlineData("00-0af7651916cd43dd8448eb211c80319z-b7ad6b7169203331-01")]
    public void Recusa_um_traceparent_invalido_sem_lancar(string? texto)
    {
        ContextoRastreio.TentarLer(texto, out var contexto).Should().BeFalse();

        contexto.Should().Be(default(ActivityContext));
    }

    [Fact]
    public void Le_o_cabecalho_em_bytes_ou_texto()
    {
        ContextoRastreio.DoCabecalho(new Dictionary<string, object?> { ["traceparent"] = Encoding.UTF8.GetBytes(Valido) })
            .Should().NotBeNull();
        ContextoRastreio.DoCabecalho(new Dictionary<string, object?> { ["traceparent"] = Valido })
            .Should().NotBeNull();
    }

    [Fact]
    public void Cabecalho_ausente_ou_invalido_e_nulo()
    {
        ContextoRastreio.DoCabecalho(null).Should().BeNull();
        ContextoRastreio.DoCabecalho(new Dictionary<string, object?>()).Should().BeNull();
        ContextoRastreio.DoCabecalho(new Dictionary<string, object?> { ["traceparent"] = "lixo"u8.ToArray() }).Should().BeNull();
        ContextoRastreio.DoCabecalho(new Dictionary<string, object?> { ["traceparent"] = 42 }).Should().BeNull();
    }
}
