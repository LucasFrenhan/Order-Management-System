using FluentAssertions;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>Regra de <c>OMS_EXIGIR_DOCKER</c> (REQ-07, FR-006).</summary>
public sealed class AmbienteDockerTestes
{
    [Theory]
    [InlineData("true")]
    [InlineData("TRUE")]
    public void Docker_e_obrigatorio_com_o_valor_true(string valor)
    {
        AmbienteDocker.Obrigatorio(valor).Should().BeFalse(); // TEMPORÁRIO: prova que o CI reprova (T024)
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("1")]
    public void Docker_nao_e_obrigatorio_com_outros_valores(string? valor)
    {
        AmbienteDocker.Obrigatorio(valor).Should().BeFalse();
    }

    [Fact]
    public void Mensagem_cita_a_variavel()
    {
        AmbienteDocker.MensagemObrigatorio.Should().Be("Docker obrigatório neste ambiente (OMS_EXIGIR_DOCKER=true).");
    }
}
