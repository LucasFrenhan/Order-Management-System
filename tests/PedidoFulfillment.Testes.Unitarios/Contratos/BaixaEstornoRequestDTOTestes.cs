using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Estornos;

namespace PedidoFulfillment.Testes.Unitarios.Contratos;

/// <summary>Validação da baixa manual (REQ-28, FR-006 e contrato).</summary>
public sealed class BaixaEstornoRequestDTOTestes
{
    private const string MotivoValido = "Devolvido por transferência pelo financeiro.";

    [Fact]
    public void Baixa_valida_com_e_sem_referencia_nao_tem_erros()
    {
        Validacao.Validar(new BaixaEstornoRequestDTO { Motivo = MotivoValido, ReferenciaExterna = "TED-778812" }).Should().BeEmpty();
        Validacao.Validar(new BaixaEstornoRequestDTO { Motivo = MotivoValido }).Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Motivo_vazio_e_rejeitado(string motivo)
    {
        Validacao.Validar(new BaixaEstornoRequestDTO { Motivo = motivo })["Motivo"].Should().Equal("O motivo é obrigatório.");
    }

    [Theory]
    [InlineData("123456789")]
    [InlineData("   123456789   ")]
    public void Motivo_com_menos_de_10_caracteres_sem_as_pontas_e_rejeitado(string motivo)
    {
        Validacao.Validar(new BaixaEstornoRequestDTO { Motivo = motivo })["Motivo"].Should().Equal(BaixaEstornoRequestDTO.MensagemMotivo);
    }

    [Fact]
    public void Motivo_de_10_e_de_500_caracteres_e_aceito()
    {
        Validacao.Validar(new BaixaEstornoRequestDTO { Motivo = "  1234567890  " }).Should().BeEmpty();
        Validacao.Validar(new BaixaEstornoRequestDTO { Motivo = new string('m', 500) }).Should().BeEmpty();
    }

    [Fact]
    public void Motivo_de_501_caracteres_e_rejeitado()
    {
        Validacao.Validar(new BaixaEstornoRequestDTO { Motivo = new string('m', 501) })["Motivo"].Should().Equal(BaixaEstornoRequestDTO.MensagemMotivo);
    }

    [Fact]
    public void Referencia_de_101_caracteres_e_rejeitada()
    {
        Validacao.Validar(new BaixaEstornoRequestDTO { Motivo = MotivoValido, ReferenciaExterna = new string('r', 101) })["ReferenciaExterna"]
            .Should().Equal("A referência externa deve ter até 100 caracteres, sem contar espaços nas pontas.");
    }

    [Fact]
    public void Valores_sao_aparados_e_referencia_so_com_espacos_vira_ausente()
    {
        var dto = new BaixaEstornoRequestDTO { Motivo = $"  {MotivoValido}  ", ReferenciaExterna = "   " };

        dto.Motivo.Should().Be(MotivoValido);
        dto.ReferenciaExterna.Should().BeNull();
    }
}
