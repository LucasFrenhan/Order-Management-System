using FluentAssertions;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Testes.Unitarios.Configuracao;

/// <summary>Validação das seções Limpeza e ReenvioDlq na partida (REQ-14, R-10, FR-021).</summary>
public sealed class OpcoesLimpezaTestes
{
    private static readonly ValidadorOpcoesLimpeza ValidadorLimpeza = new();
    private static readonly ValidadorOpcoesReenvioDlq ValidadorReenvio = new();

    [Fact]
    public void Os_padroes_da_limpeza_sao_validos()
    {
        var opcoes = new OpcoesLimpeza();

        ValidadorLimpeza.Validate(null, opcoes).Succeeded.Should().BeTrue();
        opcoes.Habilitada.Should().BeTrue();
        (opcoes.Intervalo, opcoes.TamanhoLote, opcoes.LotesPorRodada).Should().Be((TimeSpan.FromHours(1), 1000, 10));
        (opcoes.RetencaoEnviados, opcoes.RetencaoProcessados, opcoes.RetencaoChaves, opcoes.RetencaoPedidosReenvio).Should()
            .Be((TimeSpan.FromDays(7), TimeSpan.FromDays(7), TimeSpan.FromHours(48), TimeSpan.FromDays(90)));
    }

    [Fact]
    public void Os_padroes_do_reenvio_sao_validos()
    {
        var opcoes = new OpcoesReenvioDlq();

        ValidadorReenvio.Validate(null, opcoes).Succeeded.Should().BeTrue();
        (opcoes.Habilitado, opcoes.AvisoHabilitado).Should().Be((true, true));
        (opcoes.Intervalo, opcoes.MaximoReenvios, opcoes.MensagensPorPedido).Should().Be((TimeSpan.FromMinutes(1), 3, 1000));
        (opcoes.AvisoLimite, opcoes.AvisoIntervalo).Should().Be((10, TimeSpan.FromMinutes(5)));
    }

    [Theory]
    [InlineData(nameof(OpcoesLimpeza.IntervaloSegundos), 60, 86400)]
    [InlineData(nameof(OpcoesLimpeza.TamanhoLote), 1, 10000)]
    [InlineData(nameof(OpcoesLimpeza.LotesPorRodada), 1, 1000)]
    [InlineData(nameof(OpcoesLimpeza.RetencaoEnviadosHoras), 1, 8760)]
    [InlineData(nameof(OpcoesLimpeza.RetencaoProcessadosHoras), 24, 8760)]
    [InlineData(nameof(OpcoesLimpeza.RetencaoChavesHoras), 24, 8760)]
    [InlineData(nameof(OpcoesLimpeza.RetencaoPedidosReenvioHoras), 24, 87600)]
    public void Faixas_da_limpeza(string chave, int minimo, int maximo)
    {
        Validar(new OpcoesLimpeza(), chave, minimo, ValidadorLimpeza.Validate).Should().BeEmpty();
        Validar(new OpcoesLimpeza(), chave, maximo, ValidadorLimpeza.Validate).Should().BeEmpty();
        Validar(new OpcoesLimpeza(), chave, minimo - 1, ValidadorLimpeza.Validate).Should().Equal($"Limpeza:{chave} deve estar entre {minimo} e {maximo}.");
        Validar(new OpcoesLimpeza(), chave, maximo + 1, ValidadorLimpeza.Validate).Should().Equal($"Limpeza:{chave} deve estar entre {minimo} e {maximo}.");
    }

    [Theory]
    [InlineData(nameof(OpcoesReenvioDlq.IntervaloSegundos), 1, 3600)]
    [InlineData(nameof(OpcoesReenvioDlq.MaximoReenvios), 1, 100)]
    [InlineData(nameof(OpcoesReenvioDlq.MensagensPorPedido), 1, 10000)]
    [InlineData(nameof(OpcoesReenvioDlq.AvisoLimite), 1, 1000000)]
    [InlineData(nameof(OpcoesReenvioDlq.AvisoIntervaloSegundos), 10, 86400)]
    public void Faixas_do_reenvio(string chave, int minimo, int maximo)
    {
        Validar(new OpcoesReenvioDlq(), chave, minimo, ValidadorReenvio.Validate).Should().BeEmpty();
        Validar(new OpcoesReenvioDlq(), chave, maximo, ValidadorReenvio.Validate).Should().BeEmpty();
        Validar(new OpcoesReenvioDlq(), chave, minimo - 1, ValidadorReenvio.Validate).Should().Equal($"ReenvioDlq:{chave} deve estar entre {minimo} e {maximo}.");
        Validar(new OpcoesReenvioDlq(), chave, maximo + 1, ValidadorReenvio.Validate).Should().Equal($"ReenvioDlq:{chave} deve estar entre {minimo} e {maximo}.");
    }

    [Fact]
    public void Varias_falhas_sao_acumuladas_numa_so_validacao()
    {
        var opcoes = new OpcoesLimpeza { RetencaoChavesHoras = 23, TamanhoLote = 0 };

        (ValidadorLimpeza.Validate(null, opcoes).Failures ?? []).Should().BeEquivalentTo(
            "Limpeza:TamanhoLote deve estar entre 1 e 10000.",
            "Limpeza:RetencaoChavesHoras deve estar entre 24 e 8760.");
    }

    private static IEnumerable<string> Validar<T>(T opcoes, string chave, int valor, Func<string?, T, Microsoft.Extensions.Options.ValidateOptionsResult> validar)
        where T : class
    {
        typeof(T).GetProperty(chave)!.SetValue(opcoes, valor);
        return validar(null, opcoes).Failures ?? [];
    }
}
