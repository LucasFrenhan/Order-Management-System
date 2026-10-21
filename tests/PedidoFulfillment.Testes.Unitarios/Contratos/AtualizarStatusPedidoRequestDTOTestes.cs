using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Contratos;

public sealed class AtualizarStatusPedidoRequestDTOTestes
{
    [Theory]
    [InlineData(StatusPedido.Processando, null)]
    [InlineData(StatusPedido.ProntoParaEnvio, null)]
    [InlineData(StatusPedido.Despachado, "")]
    [InlineData(StatusPedido.Cancelado, "Cliente desistiu da compra.")]
    public void Atualizacoes_validas_nao_tem_erros(StatusPedido status, string? motivo)
    {
        Validacao.Validar(new AtualizarStatusPedidoRequestDTO { Status = status, Motivo = motivo }).Should().BeEmpty();
    }

    [Fact]
    public void Status_ausente_e_rejeitado()
    {
        Validacao.Validar(new AtualizarStatusPedidoRequestDTO())["Status"].Should().Contain("O status é obrigatório.");
    }

    [Fact]
    public void Pagamento_rejeitado_nao_pode_ser_definido_por_esta_operacao()
    {
        Validacao.Validar(new AtualizarStatusPedidoRequestDTO { Status = StatusPedido.PagamentoRejeitado })["Status"]
            .Should().Contain("O status PagamentoRejeitado não pode ser definido por esta operação.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Cancelamento_exige_motivo(string? motivo)
    {
        Validacao.Validar(new AtualizarStatusPedidoRequestDTO { Status = StatusPedido.Cancelado, Motivo = motivo })["Motivo"]
            .Should().Contain("O motivo é obrigatório quando o status é Cancelado.");
    }

    [Fact]
    public void Motivo_com_mais_de_500_caracteres_e_rejeitado()
    {
        var dto = new AtualizarStatusPedidoRequestDTO { Status = StatusPedido.Cancelado, Motivo = new string('m', 501) };

        Validacao.Validar(dto)["Motivo"].Should().Contain("O motivo deve ter até 500 caracteres.");
    }
}
