using FluentAssertions;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Dominio;

/// <summary>Cobrança esgotada na reconciliação (REQ-12, FR-012, data-model, R-12).</summary>
public sealed class PedidoPagamentoNaoProcessadoTestes
{
    private const string Numero = "PED-20261023-000001";
    private const string Motivo = "Não foi possível processar o pagamento.";
    private static readonly DateTimeOffset Criacao = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reserva = Criacao.AddMinutes(1);
    private static readonly DateTimeOffset Esgotamento = Criacao.AddDays(1);

    private readonly Produto a = new("A", "Produto A", null, 10m, 10, true, Criacao);
    private readonly Produto b = new("B", "Produto B", null, 25.5m, 3, true, Criacao);

    private Dictionary<Guid, Produto> Produtos => new[] { a, b }.ToDictionary(p => p.Id);

    /// <summary>Pedido de 4 A e 1 B (total 65,50), reservado e no status pedido.</summary>
    private Pedido PedidoReservado(StatusPedido status = StatusPedido.Pendente)
    {
        var pedido = new Pedido(Guid.CreateVersion7(), Numero, [(a, 4), (b, 1)], Criacao);
        pedido.ReservarEstoque(Produtos, Reserva).Should().BeEmpty();
        if (status != StatusPedido.Pendente)
        {
            pedido.MudarStatus(status, Guid.CreateVersion7(), null, Reserva);
        }

        return pedido;
    }

    [Theory]
    [InlineData(StatusPedido.Pendente)]
    [InlineData(StatusPedido.Processando)]
    [InlineData(StatusPedido.ProntoParaEnvio)]
    public void Registra_o_pagamento_devolve_o_estoque_e_cancela_o_pedido(StatusPedido status)
    {
        var pedido = PedidoReservado(status);

        pedido.RegistrarPagamentoNaoProcessado(Motivo, Produtos, Esgotamento);

        pedido.Pagamento!.Situacao.Should().Be(SituacaoPagamento.NaoProcessado);
        pedido.Pagamento.Valor.Should().Be(65.5m).And.Be(pedido.ValorTotal);
        pedido.Pagamento.IdentificadorTransacao.Should().BeNull();
        pedido.Pagamento.ProcessadoEm.Should().Be(Esgotamento);
        a.QuantidadeEstoque.Should().Be(10);
        b.QuantidadeEstoque.Should().Be(3);
        pedido.EstoqueReservado.Should().BeFalse();
        pedido.Status.Should().Be(StatusPedido.Cancelado);
        pedido.AtualizadoEm.Should().Be(Esgotamento);
        var entrada = pedido.Historico.Last();
        entrada.StatusAnterior.Should().Be(status);
        entrada.StatusNovo.Should().Be(StatusPedido.Cancelado);
        entrada.AutorId.Should().BeNull();
        entrada.Motivo.Should().Be(Motivo);
        pedido.AptoParaPagamento.Should().BeFalse();
        pedido.AptoParaOrdemEntrega.Should().BeFalse();
    }

    [Fact]
    public void O_motivo_e_aparado()
    {
        var pedido = PedidoReservado();

        pedido.RegistrarPagamentoNaoProcessado($"  {Motivo}  ", Produtos, Esgotamento);

        pedido.Historico.Last().Motivo.Should().Be(Motivo);
    }

    public static TheoryData<string> NaoAptos => ["sem reserva", "já pago", "cancelado", "produto ausente"];

    [Theory]
    [MemberData(nameof(NaoAptos))]
    public void Pedido_nao_apto_ou_sem_produto_lanca_sem_alterar_nada(string caso)
    {
        var pedido = caso switch
        {
            "sem reserva" => new Pedido(Guid.CreateVersion7(), Numero, [(a, 4), (b, 1)], Criacao),
            "cancelado" => PedidoReservado(StatusPedido.Cancelado),
            _ => PedidoReservado()
        };
        if (caso == "já pago")
        {
            pedido.AprovarPagamento("SIM-1", Reserva);
        }

        var produtos = caso == "produto ausente" ? new[] { a }.ToDictionary(p => p.Id) : Produtos;
        var status = pedido.Status;
        var pagamento = pedido.Pagamento;
        var entradas = pedido.Historico.Count;
        var reservado = pedido.EstoqueReservado;
        var estoque = (a.QuantidadeEstoque, b.QuantidadeEstoque);

        pedido.Invoking(p => p.RegistrarPagamentoNaoProcessado(Motivo, produtos, Esgotamento)).Should().Throw<InvalidOperationException>();

        pedido.Status.Should().Be(status);
        pedido.Pagamento.Should().BeSameAs(pagamento);
        pedido.Historico.Should().HaveCount(entradas);
        pedido.EstoqueReservado.Should().Be(reservado);
        (a.QuantidadeEstoque, b.QuantidadeEstoque).Should().Be(estoque);
    }
}
