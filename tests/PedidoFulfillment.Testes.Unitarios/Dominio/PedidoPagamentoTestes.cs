using FluentAssertions;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Dominio;

/// <summary>Aprovação e recusa do pagamento pelo pedido (REQ-09, data-model, R-03).</summary>
public sealed class PedidoPagamentoTestes
{
    private const string Numero = "PED-20261023-000001";
    private const string Transacao = "SIM-0199f6b12c3d7e4f8a5b6c7d8e9f0a12";
    private const string MotivoRecusa = "Pagamento recusado pelo emissor.";
    private static readonly DateTimeOffset Criacao = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reserva = Criacao.AddMinutes(1);
    private static readonly DateTimeOffset Cobranca = Criacao.AddMinutes(2);

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
    public void Pedido_reservado_sem_pagamento_e_nao_final_esta_apto(StatusPedido status) =>
        PedidoReservado(status).AptoParaPagamento.Should().BeTrue();

    [Theory]
    [InlineData(StatusPedido.Cancelado)]
    [InlineData(StatusPedido.Despachado)]
    [InlineData(StatusPedido.PagamentoRejeitado)]
    public void Pedido_em_status_final_nao_esta_apto(StatusPedido status) =>
        PedidoReservado(status).AptoParaPagamento.Should().BeFalse();

    [Fact]
    public void Pedido_sem_reserva_ou_ja_pago_nao_esta_apto()
    {
        new Pedido(Guid.CreateVersion7(), Numero, [(a, 1)], Criacao).AptoParaPagamento.Should().BeFalse();

        var pago = PedidoReservado();
        pago.AprovarPagamento(Transacao, Cobranca);
        pago.AptoParaPagamento.Should().BeFalse();
    }

    [Fact]
    public void Aprovacao_em_pendente_registra_o_pagamento_e_leva_a_processando()
    {
        var pedido = PedidoReservado();

        pedido.AprovarPagamento(Transacao, Cobranca);

        pedido.Pagamento.Should().NotBeNull();
        pedido.Pagamento!.PedidoId.Should().Be(pedido.Id);
        pedido.Pagamento.Valor.Should().Be(65.5m).And.Be(pedido.ValorTotal);
        pedido.Pagamento.Situacao.Should().Be(SituacaoPagamento.Aprovado);
        pedido.Pagamento.IdentificadorTransacao.Should().Be(Transacao);
        pedido.Pagamento.ProcessadoEm.Should().Be(Cobranca);
        pedido.Status.Should().Be(StatusPedido.Processando);
        var entrada = pedido.Historico.Last();
        entrada.StatusAnterior.Should().Be(StatusPedido.Pendente);
        entrada.StatusNovo.Should().Be(StatusPedido.Processando);
        entrada.AutorId.Should().BeNull();
        entrada.Motivo.Should().Be(Pedido.MotivoPagamentoAprovado).And.Be("Pagamento aprovado.");
        pedido.EstoqueReservado.Should().BeTrue("a mercadoria segue para o envio");
        a.QuantidadeEstoque.Should().Be(6);
        b.QuantidadeEstoque.Should().Be(2);
    }

    [Theory]
    [InlineData(StatusPedido.Processando)]
    [InlineData(StatusPedido.ProntoParaEnvio)]
    public void Aprovacao_de_pedido_ja_movido_nao_muda_o_status(StatusPedido status)
    {
        var pedido = PedidoReservado(status);
        var entradas = pedido.Historico.Count;

        pedido.AprovarPagamento(Transacao, Cobranca);

        pedido.Pagamento!.Situacao.Should().Be(SituacaoPagamento.Aprovado);
        pedido.Status.Should().Be(status);
        pedido.Historico.Should().HaveCount(entradas);
    }

    [Theory]
    [InlineData(StatusPedido.Pendente)]
    [InlineData(StatusPedido.Processando)]
    [InlineData(StatusPedido.ProntoParaEnvio)]
    public void Recusa_registra_o_pagamento_devolve_o_estoque_e_rejeita_o_pedido(StatusPedido status)
    {
        var pedido = PedidoReservado(status);

        pedido.RecusarPagamento(MotivoRecusa, Produtos, Cobranca);

        pedido.Pagamento!.Situacao.Should().Be(SituacaoPagamento.Recusado);
        pedido.Pagamento.Valor.Should().Be(pedido.ValorTotal);
        pedido.Pagamento.IdentificadorTransacao.Should().BeNull();
        pedido.Pagamento.ProcessadoEm.Should().Be(Cobranca);
        a.QuantidadeEstoque.Should().Be(10);
        b.QuantidadeEstoque.Should().Be(3);
        pedido.EstoqueReservado.Should().BeFalse();
        pedido.Status.Should().Be(StatusPedido.PagamentoRejeitado);
        var entrada = pedido.Historico.Last();
        entrada.StatusAnterior.Should().Be(status);
        entrada.StatusNovo.Should().Be(StatusPedido.PagamentoRejeitado);
        entrada.AutorId.Should().BeNull();
        entrada.Motivo.Should().Be(MotivoRecusa);
    }

    [Fact]
    public void Recusa_devolve_o_estoque_de_produto_desativado_depois_da_reserva()
    {
        var pedido = PedidoReservado();
        a.Desativar(Reserva);

        pedido.RecusarPagamento(MotivoRecusa, Produtos, Cobranca);

        a.QuantidadeEstoque.Should().Be(10);
        a.Ativo.Should().BeFalse();
    }

    public static TheoryData<string> NaoAptos => ["sem reserva", "já pago", "cancelado"];

    [Theory]
    [MemberData(nameof(NaoAptos))]
    public void Pedido_nao_apto_recusa_aprovacao_e_recusa_sem_alterar_nada(string caso)
    {
        var pedido = caso switch
        {
            "sem reserva" => new Pedido(Guid.CreateVersion7(), Numero, [(a, 4), (b, 1)], Criacao),
            "já pago" => PedidoReservado(),
            _ => PedidoReservado(StatusPedido.Cancelado)
        };
        if (caso == "já pago")
        {
            pedido.AprovarPagamento(Transacao, Cobranca);
        }

        var status = pedido.Status;
        var pagamento = pedido.Pagamento;
        var entradas = pedido.Historico.Count;
        var estoque = (a.QuantidadeEstoque, b.QuantidadeEstoque);

        pedido.Invoking(p => p.AprovarPagamento(Transacao, Cobranca)).Should().Throw<InvalidOperationException>();
        pedido.Invoking(p => p.RecusarPagamento(MotivoRecusa, Produtos, Cobranca)).Should().Throw<InvalidOperationException>();

        pedido.Status.Should().Be(status);
        pedido.Pagamento.Should().BeSameAs(pagamento);
        pedido.Historico.Should().HaveCount(entradas);
        (a.QuantidadeEstoque, b.QuantidadeEstoque).Should().Be(estoque);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Aprovacao_exige_identificador_da_transacao(string identificador)
    {
        var pedido = PedidoReservado();

        pedido.Invoking(p => p.AprovarPagamento(identificador, Cobranca)).Should().Throw<ArgumentException>();
        pedido.Pagamento.Should().BeNull();
        pedido.Status.Should().Be(StatusPedido.Pendente);
    }

    [Fact]
    public void Identificador_com_mais_de_100_caracteres_e_recusado()
    {
        var pedido = PedidoReservado();

        pedido.Invoking(p => p.AprovarPagamento(new string('x', 101), Cobranca)).Should().Throw<ArgumentException>();
        pedido.Pagamento.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Recusa_exige_motivo(string? motivo)
    {
        var pedido = PedidoReservado();

        pedido.Invoking(p => p.RecusarPagamento(motivo!, Produtos, Cobranca)).Should().Throw<ArgumentException>();
        pedido.Pagamento.Should().BeNull();
        pedido.EstoqueReservado.Should().BeTrue();
        a.QuantidadeEstoque.Should().Be(6);
    }

    [Fact]
    public void Motivo_com_mais_de_500_caracteres_e_recusado()
    {
        var pedido = PedidoReservado();

        pedido.Invoking(p => p.RecusarPagamento(new string('x', 501), Produtos, Cobranca)).Should().Throw<ArgumentException>();
        pedido.Status.Should().Be(StatusPedido.Pendente);
    }
}
