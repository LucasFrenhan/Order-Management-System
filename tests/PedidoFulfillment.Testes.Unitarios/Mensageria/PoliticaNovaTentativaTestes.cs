using System.Text;
using FluentAssertions;
using PedidoFulfillment.Infrastructure.Mensageria;

namespace PedidoFulfillment.Testes.Unitarios.Mensageria;

/// <summary>Primeira entrega + 3 novas tentativas com espera crescente, depois DLQ (REQ-06, FR-012, FR-013).</summary>
public sealed class PoliticaNovaTentativaTestes
{
    private static readonly int[] Intervalos = [1000, 2000, 4000];

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(1, 2000)]
    [InlineData(2, 4000)]
    public void Ate_tres_novas_tentativas_com_espera_crescente(int feitas, int espera)
    {
        PoliticaNovaTentativa.ProximaEspera(feitas, Intervalos).Should().Be(espera);
    }

    [Fact]
    public void Depois_de_tres_novas_tentativas_vai_para_a_DLQ()
    {
        PoliticaNovaTentativa.ProximaEspera(3, Intervalos).Should().BeNull();
    }

    [Fact]
    public void Cabecalho_ausente_conta_como_nenhuma_tentativa()
    {
        PoliticaNovaTentativa.TentativasFeitas(null).Should().Be(0);
        PoliticaNovaTentativa.TentativasFeitas(new Dictionary<string, object?>()).Should().Be(0);
    }

    [Fact]
    public void Cabecalho_e_lido_como_inteiro_ou_texto()
    {
        PoliticaNovaTentativa.TentativasFeitas(new Dictionary<string, object?> { [PoliticaNovaTentativa.CabecalhoTentativas] = 2 }).Should().Be(2);
        PoliticaNovaTentativa.TentativasFeitas(new Dictionary<string, object?> { [PoliticaNovaTentativa.CabecalhoTentativas] = 2L }).Should().Be(2);
        PoliticaNovaTentativa.TentativasFeitas(new Dictionary<string, object?> { [PoliticaNovaTentativa.CabecalhoTentativas] = Encoding.UTF8.GetBytes("1") }).Should().Be(1);
    }

    [Fact]
    public void Cabecalho_invalido_conta_como_nenhuma_tentativa()
    {
        PoliticaNovaTentativa.TentativasFeitas(new Dictionary<string, object?> { [PoliticaNovaTentativa.CabecalhoTentativas] = "abc" }).Should().Be(0);
        PoliticaNovaTentativa.TentativasFeitas(new Dictionary<string, object?> { [PoliticaNovaTentativa.CabecalhoTentativas] = -1 }).Should().Be(0);
    }
}
