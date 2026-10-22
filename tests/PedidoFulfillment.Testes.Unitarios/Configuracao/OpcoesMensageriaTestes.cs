using FluentAssertions;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Testes.Unitarios.Configuracao;

public sealed class OpcoesMensageriaTestes
{
    private static readonly ValidadorOpcoesMensageria Validador = new();

    [Fact]
    public void Os_padroes_sao_validos()
    {
        var opcoes = new OpcoesMensageria();

        Validador.Validate(null, opcoes).Succeeded.Should().BeTrue();
        opcoes.Intervalos.Should().Equal(1000, 2000, 4000);
    }

    [Theory]
    [InlineData(new[] { 1000, 2000 })]
    [InlineData(new[] { 1000, 2000, 4000, 8000 })]
    [InlineData(new[] { 1000, 1000, 4000 })]
    [InlineData(new[] { 0, 2000, 4000 })]
    public void Intervalos_precisam_ser_tres_positivos_e_crescentes(int[] intervalos)
    {
        var opcoes = new OpcoesMensageria { IntervalosNovaTentativaMs = intervalos };

        Validador.Validate(null, opcoes).Failures.Should()
            .Equal("Mensageria:IntervalosNovaTentativaMs deve ter 3 valores positivos e crescentes.");
    }

    [Fact]
    public void Lote_despacho_e_prefetch_fora_dos_limites_falham_nomeando_a_chave()
    {
        var opcoes = new OpcoesMensageria { TamanhoLote = 0, IntervaloDespachoMs = 50, PrefetchConsumidor = 0 };

        Validador.Validate(null, opcoes).Failures.Should().BeEquivalentTo(
            "Mensageria:TamanhoLote deve estar entre 1 e 500.",
            "Mensageria:IntervaloDespachoMs deve estar entre 100 e 60000.",
            "Mensageria:PrefetchConsumidor deve estar entre 1 e 1000.");
    }
}
