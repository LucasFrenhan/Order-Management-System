using FluentAssertions;
using PedidoFulfillment.Application.Pagamentos;

namespace PedidoFulfillment.Testes.Unitarios.Pagamentos;

/// <summary>Regra única do alerta do estorno (REQ-28, R-09).</summary>
public sealed class RegrasAlertaEstornoTestes
{
    private static readonly DateTimeOffset Solicitado = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly ParametrosAlertaEstorno Parametros = new(TimeSpan.FromHours(24), 3);

    [Theory]
    [InlineData(25, 3, true)]
    [InlineData(25, 4, true)]
    [InlineData(25, 2, false)]
    [InlineData(23, 3, false)]
    public void Alem_do_limite_exige_o_tempo_e_as_tentativas(int horas, int tentativas, bool esperado) =>
        RegrasAlertaEstorno.AlemDoLimite(Solicitado, tentativas, Solicitado.AddHours(horas), Parametros).Should().Be(esperado);

    [Fact]
    public void O_limite_exato_ainda_nao_conta() =>
        RegrasAlertaEstorno.AlemDoLimite(Solicitado, 3, Solicitado.AddHours(24), Parametros).Should().BeFalse();
}
