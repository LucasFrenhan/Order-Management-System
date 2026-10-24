using FluentAssertions;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Testes.Unitarios.Configuracao;

/// <summary>Validação da seção Estornos na partida (REQ-13, R-09, FR-007).</summary>
public sealed class OpcoesEstornosTestes
{
    private static readonly ValidadorOpcoesEstornos Validador = new();

    [Fact]
    public void Os_padroes_sao_validos()
    {
        var opcoes = new OpcoesEstornos();

        Validador.Validate(null, opcoes).Succeeded.Should().BeTrue();
        opcoes.Habilitado.Should().BeTrue();
        (opcoes.Intervalo, opcoes.LimiteAlerta).Should().Be((TimeSpan.FromMinutes(1), TimeSpan.FromHours(24)));
        (opcoes.TamanhoLote, opcoes.TentativasAlerta).Should().Be((100, 3));
    }

    [Theory]
    [InlineData(nameof(OpcoesEstornos.IntervaloSegundos), 1, 3600)]
    [InlineData(nameof(OpcoesEstornos.TamanhoLote), 1, 1000)]
    [InlineData(nameof(OpcoesEstornos.LimiteAlertaSegundos), 1, 2592000)]
    [InlineData(nameof(OpcoesEstornos.TentativasAlerta), 1, 100)]
    public void Limites_das_faixas_sao_aceitos_e_fora_deles_falham(string chave, int minimo, int maximo)
    {
        Validar(chave, minimo).Should().BeEmpty();
        Validar(chave, maximo).Should().BeEmpty();
        Validar(chave, minimo - 1).Should().Equal($"Estornos:{chave} deve estar entre {minimo} e {maximo}.");
        Validar(chave, maximo + 1).Should().Equal($"Estornos:{chave} deve estar entre {minimo} e {maximo}.");
    }

    [Fact]
    public void A_mensagem_cita_a_chave()
    {
        Valida(new OpcoesEstornos { TamanhoLote = 0 }).Should().Equal("Estornos:TamanhoLote deve estar entre 1 e 1000.");
    }

    [Fact]
    public void Varias_falhas_sao_acumuladas_numa_so_validacao()
    {
        var opcoes = new OpcoesEstornos { IntervaloSegundos = 0, TamanhoLote = 0, TentativasAlerta = 0 };

        Valida(opcoes).Should().BeEquivalentTo(
            "Estornos:IntervaloSegundos deve estar entre 1 e 3600.",
            "Estornos:TamanhoLote deve estar entre 1 e 1000.",
            "Estornos:TentativasAlerta deve estar entre 1 e 100.");
    }

    private static IEnumerable<string> Validar(string chave, int valor)
    {
        var opcoes = new OpcoesEstornos();
        typeof(OpcoesEstornos).GetProperty(chave)!.SetValue(opcoes, valor);
        return Valida(opcoes);
    }

    private static IEnumerable<string> Valida(OpcoesEstornos opcoes) => Validador.Validate(null, opcoes).Failures ?? [];
}
