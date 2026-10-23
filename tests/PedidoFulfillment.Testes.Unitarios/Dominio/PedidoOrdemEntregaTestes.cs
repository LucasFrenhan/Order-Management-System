using FluentAssertions;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Dominio;

/// <summary>Criação da ordem de entrega pelo pedido pago (REQ-10, data-model, R-02).</summary>
public sealed class PedidoOrdemEntregaTestes
{
    private const string Numero = "PED-20261023-000001";
    private const string Transacao = "SIM-0199f6b12c3d7e4f8a5b6c7d8e9f0a12";
    private static readonly DateTimeOffset Criacao = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reserva = Criacao.AddMinutes(1);
    private static readonly DateTimeOffset Cobranca = Criacao.AddMinutes(2);
    private static readonly DateTimeOffset Entrega = Criacao.AddMinutes(3);

    private readonly Produto a = new("A", "Produto A", null, 10m, 10, true, Criacao);
    private readonly Produto b = new("B", "Produto B", null, 25.5m, 3, true, Criacao);

    private Dictionary<Guid, Produto> Produtos => new[] { a, b }.ToDictionary(p => p.Id);

    private Pedido PedidoReservado()
    {
        var pedido = new Pedido(Guid.CreateVersion7(), Numero, [(a, 4), (b, 1)], Criacao);
        pedido.ReservarEstoque(Produtos, Reserva).Should().BeEmpty();
        return pedido;
    }

    /// <summary>Pedido de 4 A e 1 B, reservado, pago (Processando) e, se pedido, movido para outro status.</summary>
    private Pedido PedidoPago(StatusPedido status = StatusPedido.Processando)
    {
        var pedido = PedidoReservado();
        pedido.AprovarPagamento(Transacao, Cobranca);
        if (status != StatusPedido.Processando)
        {
            pedido.MudarStatus(status, Guid.CreateVersion7(), null, Cobranca);
        }

        return pedido;
    }

    /// <summary>Casos não aptos fora do status (sem reserva, sem pagamento, recusado e já com ordem).</summary>
    public static TheoryData<string> CasosNaoAptos => ["sem reserva", "sem pagamento", "pagamento recusado", "com ordem"];

    private Pedido PedidoNaoApto(string caso)
    {
        switch (caso)
        {
            case "sem reserva":
                var semReserva = PedidoPago();
                semReserva.DevolverEstoque(Produtos, Cobranca);
                return semReserva;
            case "sem pagamento":
                var semPagamento = PedidoReservado();
                semPagamento.MudarStatus(StatusPedido.Processando, Guid.CreateVersion7(), null, Cobranca);
                return semPagamento;
            case "pagamento recusado":
                var recusado = PedidoReservado();
                recusado.RecusarPagamento("Pagamento recusado pelo emissor.", Produtos, Cobranca);
                recusado.ReservarEstoque(Produtos, Cobranca).Should().BeEmpty();
                recusado.MudarStatus(StatusPedido.Processando, Guid.CreateVersion7(), null, Cobranca);
                return recusado;
            default:
                var comOrdem = PedidoPago();
                comOrdem.CriarOrdemEntrega(Cobranca);
                return comOrdem;
        }
    }

    [Theory]
    [InlineData(StatusPedido.Processando)]
    [InlineData(StatusPedido.ProntoParaEnvio)]
    public void Pedido_pago_reservado_sem_ordem_esta_apto(StatusPedido status) =>
        PedidoPago(status).AptoParaOrdemEntrega.Should().BeTrue();

    [Theory]
    [InlineData(StatusPedido.Pendente)]
    [InlineData(StatusPedido.Cancelado)]
    [InlineData(StatusPedido.Despachado)]
    [InlineData(StatusPedido.PagamentoRejeitado)]
    public void Pedido_fora_de_processando_ou_pronto_nao_esta_apto(StatusPedido status) =>
        PedidoPago(status).AptoParaOrdemEntrega.Should().BeFalse();

    [Theory]
    [MemberData(nameof(CasosNaoAptos))]
    public void Pedido_sem_reserva_sem_pagamento_aprovado_ou_com_ordem_nao_esta_apto(string caso) =>
        PedidoNaoApto(caso).AptoParaOrdemEntrega.Should().BeFalse();

    [Fact]
    public void Criacao_em_processando_guarda_a_ordem_e_leva_a_pronto_para_envio()
    {
        var pedido = PedidoPago();
        var pagamento = pedido.Pagamento;

        var ordem = pedido.CriarOrdemEntrega(Entrega);

        pedido.OrdemEntrega.Should().BeSameAs(ordem);
        ordem.PedidoId.Should().Be(pedido.Id);
        ordem.CriadoEm.Should().Be(Entrega);
        ordem.AtualizadoEm.Should().Be(Entrega);
        ordem.Transportadora.Should().BeNull();
        ordem.CodigoRastreio.Should().BeNull();
        ordem.DespachadoEm.Should().BeNull();
        pedido.Status.Should().Be(StatusPedido.ProntoParaEnvio);
        pedido.AtualizadoEm.Should().Be(Entrega);
        var entrada = pedido.Historico.Last();
        entrada.StatusAnterior.Should().Be(StatusPedido.Processando);
        entrada.StatusNovo.Should().Be(StatusPedido.ProntoParaEnvio);
        entrada.AutorId.Should().BeNull();
        entrada.Motivo.Should().Be(Pedido.MotivoOrdemEntregaCriada).And.Be("Ordem de entrega criada.");
        pedido.EstoqueReservado.Should().BeTrue();
        pedido.Pagamento.Should().BeSameAs(pagamento);
        pedido.Pagamento!.Situacao.Should().Be(SituacaoPagamento.Aprovado);
        a.QuantidadeEstoque.Should().Be(6);
        b.QuantidadeEstoque.Should().Be(2);
    }

    [Fact]
    public void Criacao_em_pronto_para_envio_nao_muda_o_status()
    {
        var pedido = PedidoPago(StatusPedido.ProntoParaEnvio);
        var entradas = pedido.Historico.Count;

        var ordem = pedido.CriarOrdemEntrega(Entrega);

        pedido.OrdemEntrega.Should().BeSameAs(ordem);
        pedido.Status.Should().Be(StatusPedido.ProntoParaEnvio);
        pedido.Historico.Should().HaveCount(entradas);
        pedido.AtualizadoEm.Should().Be(Entrega, "a versão do pedido precisa mudar (R-04)");
    }

    [Theory]
    [InlineData(StatusPedido.Pendente)]
    [InlineData(StatusPedido.Cancelado)]
    [InlineData(StatusPedido.Despachado)]
    [InlineData(StatusPedido.PagamentoRejeitado)]
    public void Criacao_fora_do_status_falha_sem_alterar_nada(StatusPedido status) =>
        CriacaoFalhaSemAlterar(PedidoPago(status));

    [Theory]
    [MemberData(nameof(CasosNaoAptos))]
    public void Criacao_de_pedido_nao_apto_falha_sem_alterar_nada(string caso) =>
        CriacaoFalhaSemAlterar(PedidoNaoApto(caso));

    [Fact]
    public void Segunda_criacao_falha_e_mantem_a_primeira_ordem()
    {
        var pedido = PedidoPago();
        var primeira = pedido.CriarOrdemEntrega(Entrega);

        var segunda = () => pedido.CriarOrdemEntrega(Entrega.AddMinutes(1));

        segunda.Should().Throw<InvalidOperationException>();
        pedido.OrdemEntrega.Should().BeSameAs(primeira);
    }

    private static void CriacaoFalhaSemAlterar(Pedido pedido)
    {
        var ordem = pedido.OrdemEntrega;
        var status = pedido.Status;
        var entradas = pedido.Historico.Count;
        var atualizadoEm = pedido.AtualizadoEm;

        var criar = () => pedido.CriarOrdemEntrega(Entrega);

        criar.Should().Throw<InvalidOperationException>().WithMessage($"*{Numero}*");
        pedido.OrdemEntrega.Should().BeSameAs(ordem);
        pedido.Status.Should().Be(status);
        pedido.Historico.Should().HaveCount(entradas);
        pedido.AtualizadoEm.Should().Be(atualizadoEm);
    }
}
