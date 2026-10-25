using FluentAssertions;
using PedidoFulfillment.Application.Mensageria;

namespace PedidoFulfillment.Testes.Unitarios.Mensageria;

/// <summary>Filtro do reenvio da DLQ (REQ-14, R-05).</summary>
public sealed class FiltroReenvioTestes
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Filtro_vazio_atende_a_tudo_inclusive_sem_cabecalhos()
    {
        var filtro = new FiltroReenvio(null, null, null, null);

        filtro.Atende("pedido.criado", T0, "x").Should().BeTrue();
        filtro.Atende(null, null, null).Should().BeTrue();
        filtro.ToString().Should().Be("nenhum");
    }

    [Theory]
    [InlineData("pedido.criado", true)]
    [InlineData("estoque.reservado", false)]
    [InlineData("PEDIDO.CRIADO", false)]
    [InlineData(null, false)]
    public void Tipo_e_por_igualdade_ordinal(string? tipo, bool atende) =>
        new FiltroReenvio("pedido.criado", null, null, null).Atende(tipo, T0, null).Should().Be(atende);

    [Theory]
    [InlineData(-61, false)]
    [InlineData(-60, true)]
    [InlineData(0, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    public void Periodo_inclui_as_pontas(int segundos, bool atende) =>
        new FiltroReenvio(null, T0.AddSeconds(-60), T0.AddSeconds(60), null).Atende(null, T0.AddSeconds(segundos), null).Should().Be(atende);

    [Fact]
    public void Periodo_aberto_numa_ponta_e_sem_o_instante_nao_atende()
    {
        new FiltroReenvio(null, T0, null, null).Atende(null, T0.AddDays(10), null).Should().BeTrue();
        new FiltroReenvio(null, null, T0, null).Atende(null, T0.AddDays(-10), null).Should().BeTrue();
        new FiltroReenvio(null, null, T0, null).Atende(null, T0.AddSeconds(1), null).Should().BeFalse();
        new FiltroReenvio(null, T0, null, null).Atende(null, null, null).Should().BeFalse("sem x-falhou-em");
    }

    [Theory]
    [InlineData("TimeoutException: sem resposta", true)]
    [InlineData("timeoutexception", true)]
    [InlineData("Contrato inválido", false)]
    [InlineData(null, false)]
    public void Motivo_por_trecho_sem_diferenciar_maiusculas(string? motivo, bool atende) =>
        new FiltroReenvio(null, null, null, "Timeout").Atende(null, null, motivo).Should().Be(atende);

    [Fact]
    public void Campos_se_combinam()
    {
        var filtro = new FiltroReenvio("pedido.criado", T0.AddHours(-1), T0, "Timeout");

        filtro.Atende("pedido.criado", T0, "Timeout").Should().BeTrue();
        filtro.Atende("estoque.reservado", T0, "Timeout").Should().BeFalse();
        filtro.Atende("pedido.criado", T0.AddHours(1), "Timeout").Should().BeFalse();
        filtro.Atende("pedido.criado", T0, "outro").Should().BeFalse();
        filtro.ToString().Should().Contain("tipo=pedido.criado").And.Contain("motivo~Timeout");
    }
}
