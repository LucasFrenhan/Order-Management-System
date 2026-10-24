using FluentAssertions;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Dominio;

/// <summary>Cancelamento com estorno e os métodos de estorno do agregado (REQ-13, R-01, R-02 e R-07).</summary>
public sealed class PedidoCancelamentoTestes
{
    private const string Numero = "PED-20261024-000001";
    private const string Transacao = "SIM-0199f6b12c3d7e4f8a5b6c7d8e9f0a12";
    private const string Motivo = "Cliente desistiu da compra.";
    private static readonly DateTimeOffset Criacao = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reserva = Criacao.AddMinutes(1);
    private static readonly DateTimeOffset Cobranca = Criacao.AddMinutes(2);
    private static readonly DateTimeOffset Cancelamento = Criacao.AddMinutes(10);
    private static readonly Guid Operador = Guid.CreateVersion7();

    private readonly Produto a = new("A", "Produto A", null, 10m, 10, true, Criacao);
    private readonly Produto b = new("B", "Produto B", null, 25.5m, 3, true, Criacao);

    private Dictionary<Guid, Produto> Produtos => new[] { a, b }.ToDictionary(p => p.Id);

    private Pedido PedidoNovo() => new(Guid.CreateVersion7(), Numero, [(a, 4), (b, 1)], Criacao);

    private Pedido PedidoReservado(StatusPedido status = StatusPedido.Pendente)
    {
        var pedido = PedidoNovo();
        pedido.ReservarEstoque(Produtos, Reserva).Should().BeEmpty();
        if (status != StatusPedido.Pendente)
        {
            pedido.MudarStatus(status, Operador, null, Reserva);
        }

        return pedido;
    }

    private Pedido PedidoPago()
    {
        var pedido = PedidoReservado();
        pedido.AprovarPagamento(Transacao, Cobranca);
        return pedido;
    }

    [Fact]
    public void Pendente_sem_reserva_nem_pagamento_so_muda_o_status()
    {
        var pedido = PedidoNovo();

        var resultado = pedido.Cancelar(Operador, $"  {Motivo}  ", Produtos, Cancelamento);

        resultado.Should().Be(new ResultadoCancelamento(false, false, false));
        pedido.Status.Should().Be(StatusPedido.Cancelado);
        pedido.Pagamento.Should().BeNull();
        pedido.AtualizadoEm.Should().Be(Cancelamento);
        var entrada = pedido.Historico.Last();
        entrada.StatusAnterior.Should().Be(StatusPedido.Pendente);
        entrada.StatusNovo.Should().Be(StatusPedido.Cancelado);
        entrada.AutorId.Should().Be(Operador);
        entrada.Motivo.Should().Be(Motivo);
        a.QuantidadeEstoque.Should().Be(10);
    }

    [Theory]
    [InlineData(StatusPedido.Pendente)]
    [InlineData(StatusPedido.Processando)]
    public void Com_reserva_e_sem_pagamento_devolve_o_estoque_sem_estorno(StatusPedido status)
    {
        var pedido = PedidoReservado(status);
        a.QuantidadeEstoque.Should().Be(6);

        var resultado = pedido.Cancelar(Operador, Motivo, Produtos, Cancelamento);

        resultado.Should().Be(new ResultadoCancelamento(true, false, false));
        pedido.EstoqueReservado.Should().BeFalse();
        a.QuantidadeEstoque.Should().Be(10);
        b.QuantidadeEstoque.Should().Be(3);
        pedido.Pagamento.Should().BeNull();
        pedido.Status.Should().Be(StatusPedido.Cancelado);
    }

    [Fact]
    public void Pago_devolve_o_estoque_e_pede_o_estorno_na_mesma_operacao()
    {
        var pedido = PedidoPago();

        var resultado = pedido.Cancelar(Operador, Motivo, Produtos, Cancelamento);

        resultado.Should().Be(new ResultadoCancelamento(true, true, false));
        pedido.Status.Should().Be(StatusPedido.Cancelado);
        pedido.EstoqueReservado.Should().BeFalse();
        a.QuantidadeEstoque.Should().Be(10);
        pedido.Pagamento!.Situacao.Should().Be(SituacaoPagamento.EstornoPendente);
        pedido.Pagamento.EstornoSolicitadoEm.Should().Be(Cancelamento);
        pedido.Pagamento.IdentificadorTransacao.Should().Be(Transacao);
        pedido.AtualizadoEm.Should().Be(Cancelamento);
    }

    [Fact]
    public void Pronto_para_envio_com_ordem_cancela_a_ordem_junto_com_o_estorno_e_o_estoque()
    {
        // REQ-13, US2 e FR-008.
        var pedido = PedidoPago();
        var ordem = pedido.CriarOrdemEntrega(Cobranca.AddMinutes(1));

        var resultado = pedido.Cancelar(Operador, Motivo, Produtos, Cancelamento);

        resultado.Should().Be(new ResultadoCancelamento(true, true, true));
        ordem.CanceladaEm.Should().Be(Cancelamento);
        ordem.AtualizadoEm.Should().Be(Cancelamento);
        ordem.DespachadoEm.Should().BeNull();
        pedido.Status.Should().Be(StatusPedido.Cancelado);
        pedido.Pagamento!.Situacao.Should().Be(SituacaoPagamento.EstornoPendente);
        a.QuantidadeEstoque.Should().Be(10);
    }

    [Fact]
    public void Ordem_despachada_impede_o_cancelamento_sem_alterar_nada()
    {
        // Só por fora do fluxo (status mexido à mão): o pedido despachado já é recusado pelo status.
        var pedido = PedidoPago();
        pedido.CriarOrdemEntrega(Cobranca.AddMinutes(1)).RegistrarDespacho("Correios", "BR1", Cobranca.AddMinutes(2));

        var cancelar = () => pedido.Cancelar(Operador, Motivo, Produtos, Cancelamento);

        cancelar.Should().Throw<InvalidOperationException>();
        pedido.Status.Should().Be(StatusPedido.ProntoParaEnvio);
        pedido.EstoqueReservado.Should().BeTrue();
        pedido.Pagamento!.Situacao.Should().Be(SituacaoPagamento.Aprovado);
        pedido.OrdemEntrega!.CanceladaEm.Should().BeNull();
    }

    [Fact]
    public void Sem_ordem_o_resultado_nao_marca_ordem_cancelada()
    {
        var pedido = PedidoPago();

        pedido.Cancelar(Operador, Motivo, Produtos, Cancelamento).OrdemCancelada.Should().BeFalse();
        pedido.OrdemEntrega.Should().BeNull();
    }

    [Fact]
    public void Sem_autor_grava_o_historico_sem_autor()
    {
        var pedido = PedidoPago();

        pedido.Cancelar(null, Motivo, Produtos, Cancelamento);

        pedido.Historico.Last().AutorId.Should().BeNull();
        pedido.Historico.Last().Motivo.Should().Be(Motivo);
    }

    [Theory]
    [InlineData(StatusPedido.Despachado)]
    [InlineData(StatusPedido.Cancelado)]
    [InlineData(StatusPedido.PagamentoRejeitado)]
    public void Status_final_e_recusado_sem_alterar_nada(StatusPedido status)
    {
        var pedido = PedidoPago();
        pedido.MudarStatus(status, Operador, null, Cobranca);
        var historico = pedido.Historico.Count;

        var cancelar = () => pedido.Cancelar(Operador, Motivo, Produtos, Cancelamento);

        cancelar.Should().Throw<InvalidOperationException>();
        pedido.Status.Should().Be(status);
        pedido.EstoqueReservado.Should().BeTrue();
        a.QuantidadeEstoque.Should().Be(6);
        pedido.Pagamento!.Situacao.Should().Be(SituacaoPagamento.Aprovado);
        pedido.Historico.Should().HaveCount(historico);
        pedido.AtualizadoEm.Should().Be(Cobranca);
    }

    [Fact]
    public void Produto_ausente_com_reserva_e_recusado_sem_alterar_nada()
    {
        var pedido = PedidoPago();
        var soA = new[] { a }.ToDictionary(p => p.Id);

        var cancelar = () => pedido.Cancelar(Operador, Motivo, soA, Cancelamento);

        cancelar.Should().Throw<InvalidOperationException>();
        pedido.Status.Should().Be(StatusPedido.Processando);
        pedido.EstoqueReservado.Should().BeTrue();
        a.QuantidadeEstoque.Should().Be(6);
        pedido.Pagamento!.Situacao.Should().Be(SituacaoPagamento.Aprovado);
    }

    [Fact]
    public void Motivo_acima_de_500_caracteres_e_recusado_sem_alterar_nada()
    {
        var pedido = PedidoPago();

        var cancelar = () => pedido.Cancelar(Operador, new string('m', 501), Produtos, Cancelamento);

        cancelar.Should().Throw<ArgumentException>();
        pedido.Status.Should().Be(StatusPedido.Processando);
        pedido.EstoqueReservado.Should().BeTrue();
        pedido.Pagamento!.Situacao.Should().Be(SituacaoPagamento.Aprovado);
    }

    [Fact]
    public void ConfirmarEstorno_leva_o_pagamento_a_estornado_e_atualiza_o_pedido()
    {
        var pedido = PedidoPago();
        pedido.Cancelar(Operador, Motivo, Produtos, Cancelamento);
        var confirmacao = Cancelamento.AddMinutes(1);

        pedido.ConfirmarEstorno("SIM-EST-1", confirmacao);

        pedido.Pagamento!.Situacao.Should().Be(SituacaoPagamento.Estornado);
        pedido.Pagamento.IdentificadorEstorno.Should().Be("SIM-EST-1");
        pedido.AtualizadoEm.Should().Be(confirmacao);
        pedido.Status.Should().Be(StatusPedido.Cancelado);
    }

    [Fact]
    public void RegistrarFalhaEstorno_conta_a_tentativa_e_atualiza_o_pedido()
    {
        var pedido = PedidoPago();
        pedido.Cancelar(Operador, Motivo, Produtos, Cancelamento);
        var falha = Cancelamento.AddMinutes(1);

        pedido.RegistrarFalhaEstorno(falha);

        pedido.Pagamento!.TentativasEstorno.Should().Be(1);
        pedido.Pagamento.UltimaFalhaEstornoEm.Should().Be(falha);
        pedido.AtualizadoEm.Should().Be(falha);
    }

    [Fact]
    public void Metodos_de_estorno_sem_estorno_pendente_sao_recusados()
    {
        var semPagamento = PedidoNovo();
        var pago = PedidoPago();

        ((Action)(() => semPagamento.ConfirmarEstorno("SIM-EST-1", Cancelamento))).Should().Throw<InvalidOperationException>();
        ((Action)(() => semPagamento.RegistrarFalhaEstorno(Cancelamento))).Should().Throw<InvalidOperationException>();
        ((Action)(() => pago.ConfirmarEstorno("SIM-EST-1", Cancelamento))).Should().Throw<InvalidOperationException>();
        ((Action)(() => pago.RegistrarFalhaEstorno(Cancelamento))).Should().Throw<InvalidOperationException>();
        pago.AtualizadoEm.Should().Be(Cobranca);
    }

    [Fact]
    public void Cobranca_apos_cancelamento_registra_o_pagamento_ja_com_estorno_pendente()
    {
        var pedido = PedidoReservado();
        pedido.Cancelar(Operador, Motivo, Produtos, Cancelamento);
        var historico = pedido.Historico.Count;
        var cobranca = Cancelamento.AddSeconds(5);

        pedido.RegistrarCobrancaAposCancelamento($"  {Transacao}  ", cobranca);

        pedido.Pagamento.Should().NotBeNull();
        pedido.Pagamento!.Valor.Should().Be(pedido.ValorTotal);
        pedido.Pagamento.IdentificadorTransacao.Should().Be(Transacao);
        pedido.Pagamento.Situacao.Should().Be(SituacaoPagamento.EstornoPendente);
        pedido.Pagamento.EstornoSolicitadoEm.Should().Be(cobranca);
        pedido.Status.Should().Be(StatusPedido.Cancelado);
        pedido.Historico.Should().HaveCount(historico);
        pedido.AtualizadoEm.Should().Be(cobranca);
    }

    [Fact]
    public void Cobranca_apos_cancelamento_exige_pedido_cancelado_e_sem_pagamento()
    {
        var naoCancelado = PedidoReservado();
        var jaPago = PedidoPago();
        jaPago.Cancelar(Operador, Motivo, Produtos, Cancelamento);

        ((Action)(() => naoCancelado.RegistrarCobrancaAposCancelamento(Transacao, Cancelamento))).Should().Throw<InvalidOperationException>();
        ((Action)(() => jaPago.RegistrarCobrancaAposCancelamento(Transacao, Cancelamento))).Should().Throw<InvalidOperationException>();
        naoCancelado.Pagamento.Should().BeNull();
        jaPago.Pagamento!.Situacao.Should().Be(SituacaoPagamento.EstornoPendente);
    }
}
