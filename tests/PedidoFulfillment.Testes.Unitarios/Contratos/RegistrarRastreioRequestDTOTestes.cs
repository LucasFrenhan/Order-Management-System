using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Contratos;

public sealed class RegistrarRastreioRequestDTOTestes
{
    [Fact]
    public void Rastreio_valido_nao_tem_erros()
    {
        Validacao.Validar(new RegistrarRastreioRequestDTO { Transportadora = "Correios", CodigoRastreio = "BR123" }).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Transportadora_vazia_e_rejeitada(string transportadora)
    {
        Validacao.Validar(new RegistrarRastreioRequestDTO { Transportadora = transportadora, CodigoRastreio = "BR123" })["Transportadora"]
            .Should().Equal("A transportadora é obrigatória.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Codigo_vazio_e_rejeitado(string codigo)
    {
        Validacao.Validar(new RegistrarRastreioRequestDTO { Transportadora = "Correios", CodigoRastreio = codigo })["CodigoRastreio"]
            .Should().Equal("O código de rastreio é obrigatório.");
    }

    [Fact]
    public void Transportadora_longa_demais_e_rejeitada()
    {
        Validacao.Validar(new RegistrarRastreioRequestDTO { Transportadora = new string('t', 101), CodigoRastreio = "BR123" })["Transportadora"]
            .Should().Equal("A transportadora deve ter até 100 caracteres.");
    }

    [Fact]
    public void Codigo_longo_demais_e_rejeitado()
    {
        Validacao.Validar(new RegistrarRastreioRequestDTO { Transportadora = "Correios", CodigoRastreio = new string('c', 51) })["CodigoRastreio"]
            .Should().Equal("O código de rastreio deve ter até 50 caracteres.");
    }

    [Fact]
    public void Espacos_nas_pontas_nao_contam_no_tamanho()
    {
        var dto = new RegistrarRastreioRequestDTO { Transportadora = $"  {new string('t', 100)}  ", CodigoRastreio = " BR123 " };

        Validacao.Validar(dto).Should().BeEmpty();
        dto.CodigoRastreio.Should().Be("BR123");
    }

    [Fact]
    public void Valores_nulos_sao_obrigatorios()
    {
        var erros = Validacao.Validar(new RegistrarRastreioRequestDTO { Transportadora = null!, CodigoRastreio = null! });

        erros["Transportadora"].Should().Equal("A transportadora é obrigatória.");
        erros["CodigoRastreio"].Should().Equal("O código de rastreio é obrigatório.");
    }
}
