using System.Text;
using FluentAssertions;
using PedidoFulfillment.Infrastructure.Mensageria;

namespace PedidoFulfillment.Testes.Unitarios.Mensageria;

/// <summary>Cabeçalhos da DLQ e da cópia reenviada (REQ-14, R-06).</summary>
public sealed class CabecalhosReenvioTestes
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static byte[] Bytes(string texto) => Encoding.UTF8.GetBytes(texto);

    [Theory]
    [InlineData(null, 0)]
    [InlineData(2, 2)]
    [InlineData(5L, 5)]
    [InlineData("3", 3)]
    [InlineData("abc", 0)]
    [InlineData(-1, 0)]
    public void Reenvios_feitos_aceitam_os_formatos_do_broker(object? valor, int esperado)
    {
        var cabecalhos = new Dictionary<string, object?>();
        if (valor is not null)
        {
            cabecalhos[CabecalhosReenvio.Reenvios] = valor is string texto ? Bytes(texto) : valor;
        }

        CabecalhosReenvio.ReenviosFeitos(cabecalhos).Should().Be(esperado);
        CabecalhosReenvio.ReenviosFeitos(null).Should().Be(0);
    }

    [Fact]
    public void Motivo_e_instante_da_falha_sao_lidos_dos_bytes()
    {
        var cabecalhos = new Dictionary<string, object?>
        {
            ["x-motivo"] = Bytes("TimeoutException: sem resposta"),
            ["x-falhou-em"] = Bytes(T0.ToString("O"))
        };

        CabecalhosReenvio.Motivo(cabecalhos).Should().Be("TimeoutException: sem resposta");
        CabecalhosReenvio.FalhouEm(cabecalhos).Should().Be(T0);
        CabecalhosReenvio.FalhouEm(new Dictionary<string, object?> { ["x-falhou-em"] = Bytes("ontem") }).Should().BeNull();
        CabecalhosReenvio.Motivo(null).Should().BeNull();
    }

    [Fact]
    public void A_copia_reenviada_recomeca_as_tentativas_e_conta_o_reenvio()
    {
        var pedido = Guid.CreateVersion7();
        var originais = new Dictionary<string, object?>
        {
            ["x-tentativas"] = 3,
            ["x-ultimo-erro"] = Bytes("erro"),
            ["x-motivo"] = Bytes("motivo"),
            ["x-falhou-em"] = Bytes(T0.ToString("O")),
            [CabecalhosReenvio.Reenvios] = 1,
            ["x-outro"] = Bytes("mantido")
        };

        var copia = CabecalhosReenvio.ParaReenvio(originais, pedido, T0.AddMinutes(5));

        copia.Should().NotContainKeys("x-tentativas", "x-ultimo-erro");
        copia[CabecalhosReenvio.Reenvios].Should().Be(2);
        copia[CabecalhosReenvio.ReenviadoEm].Should().Be(T0.AddMinutes(5).ToString("O"));
        copia[CabecalhosReenvio.PedidoReenvio].Should().Be(pedido.ToString());
        copia.Should().ContainKeys("x-motivo", "x-falhou-em", "x-outro");
        originais.Should().ContainKey("x-tentativas", "os originais não são alterados");
    }
}
