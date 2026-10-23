using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;
using PedidoFulfillment.Testes.Unitarios.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Pagamentos;

/// <summary>Decisão do consumidor de pagamento (REQ-09, research R-05; contracts/consumidor-pagamento.md).</summary>
public sealed class ProcessamentoPagamentoTestes
{
    private const string Transacao = "SIM-0199f6b12c3d7e4f8a5b6c7d8e9f0a12";
    private const string MotivoRecusa = "Pagamento recusado pelo emissor.";
    private static readonly DateTimeOffset Criacao = new(2026, 10, 23, 15, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Agora = Criacao.AddSeconds(2);
    private static readonly Guid PedidoId = Guid.CreateVersion7();

    private readonly ArmazenamentoPedidosFalso armazenamento = new();
    private readonly GatewayPagamentoFalso gateway = new();
    private readonly LoggerFalso<ProcessamentoPagamento> logger = new();
    private readonly Produto a;
    private readonly Produto b;

    public ProcessamentoPagamentoTestes()
    {
        // Estoque já abatido pela reserva do pedido (3 de A e 2 de B).
        a = new("SKU-A", "Produto A", null, 10m, 7, true, Criacao, Guid.Parse("0192b6a0-0000-7000-8000-000000000002"));
        b = new("SKU-B", "Produto B", null, 12.5m, 3, true, Criacao, Guid.Parse("0192b6a0-0000-7000-8000-000000000001"));
        armazenamento.Produtos.AddRange([a, b]);
    }

    private ProcessamentoPagamento Consumidor() => new(armazenamento, gateway, new RelogioFixo(Agora), logger);

    /// <summary>Guarda o pedido (3 de A e 2 de B, total 55,00); cada leitura monta uma instância nova.</summary>
    private void PedidoEm(StatusPedido status = StatusPedido.Pendente, bool reservado = true, bool pago = false) =>
        armazenamento.GuardarPedido(PedidoId, () => Montar(status, reservado, pago));

    private Pedido Montar(StatusPedido status, bool reservado, bool pago)
    {
        var pedido = new Pedido(Guid.CreateVersion7(), "PED-20261023-000001", [(a, 3), (b, 2)], Criacao);
        typeof(Pedido).GetProperty(nameof(Pedido.Id))!.SetValue(pedido, PedidoId);
        typeof(Pedido).GetProperty(nameof(Pedido.EstoqueReservado))!.SetValue(pedido, reservado);
        if (pago)
        {
            pedido.AprovarPagamento(Transacao, Criacao);
        }

        StatusPedido[] caminho = status switch
        {
            StatusPedido.Pendente => [],
            StatusPedido.Processando when pago => [],
            StatusPedido.Processando => [StatusPedido.Processando],
            StatusPedido.ProntoParaEnvio => [StatusPedido.Processando, StatusPedido.ProntoParaEnvio],
            StatusPedido.Despachado => [StatusPedido.Processando, StatusPedido.ProntoParaEnvio, StatusPedido.Despachado],
            _ => [status]
        };
        foreach (var passo in caminho)
        {
            pedido.MudarStatus(passo, Guid.CreateVersion7(), null, Criacao);
        }

        return pedido;
    }

    private static EnvelopeEvento<EstoqueReservado> Evento(Guid pedidoId) => EnvelopeEvento.Criar(pedidoId, new EstoqueReservado
    {
        PedidoId = pedidoId,
        Itens = [new ItemEstoqueReservado { ProdutoId = Guid.CreateVersion7(), Quantidade = 1 }]
    }, Criacao);

    private Task Manipular(Guid? pedidoId = null) => Consumidor().ManipularAsync(Evento(pedidoId ?? PedidoId), CancellationToken.None);

    private Pedido Gravado() => armazenamento.ObterPedidoAsync(PedidoId, false, CancellationToken.None).Result!;

    private int Estoque(Produto produto) => armazenamento.Produtos.Single(p => p.Id == produto.Id).QuantidadeEstoque;

    private EnvelopeEvento<T> EventoGravado<T>(string tipo)
    {
        var evento = armazenamento.EventosGravados.Should().ContainSingle().Subject;
        evento.Tipo.Should().Be(tipo);
        return SerializacaoEventos.Desserializar<T>(evento.Conteudo);
    }

    [Fact]
    public void Consome_estoque_reservado_na_fila_de_pagamento()
    {
        var consumidor = Consumidor();

        consumidor.Consumidor.Should().Be("pagamento");
        consumidor.Tipos.Should().Equal(TiposEvento.EstoqueReservado);
    }

    [Fact]
    public async Task Pagamento_aprovado_leva_o_pedido_a_processando_e_publica_pagamento_aprovado()
    {
        PedidoEm();
        gateway.Proximo = ResultadoCobranca.Aprovada(Transacao);

        await Manipular();

        gateway.Chamadas.Should().Equal((PedidoId, 55m));
        var pedido = Gravado();
        pedido.Status.Should().Be(StatusPedido.Processando);
        pedido.Pagamento!.Situacao.Should().Be(SituacaoPagamento.Aprovado);
        pedido.Pagamento.IdentificadorTransacao.Should().Be(Transacao);
        pedido.Pagamento.ProcessadoEm.Should().Be(Agora);
        pedido.EstoqueReservado.Should().BeTrue();
        armazenamento.LeiturasProdutosParaAlterar.Should().Be(0, "a aprovação não mexe em estoque");
        (Estoque(a), Estoque(b)).Should().Be((7, 3));
        armazenamento.Gravacoes.Should().Be(1);

        var envelope = EventoGravado<PagamentoAprovado>(TiposEvento.PagamentoAprovado);
        envelope.IdCorrelacao.Should().Be(PedidoId);
        envelope.OcorridoEm.Should().Be(Agora);
        (envelope.Dados.PedidoId, envelope.Dados.Valor, envelope.Dados.IdentificadorTransacao).Should().Be((PedidoId, 55m, Transacao));
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 7001);
    }

    [Fact]
    public async Task Pedido_inexistente_falha_sem_cobrar_nem_gravar()
    {
        await FluentActions.Awaiting(() => Manipular(Guid.CreateVersion7())).Should().ThrowAsync<InvalidOperationException>();

        gateway.Chamadas.Should().BeEmpty();
        armazenamento.Gravacoes.Should().Be(0);
        armazenamento.EventosGravados.Should().BeEmpty();
    }
}

/// <summary>Gateway que devolve o resultado programado e guarda as cobranças pedidas.</summary>
public sealed class GatewayPagamentoFalso : IGatewayPagamento
{
    public ResultadoCobranca Proximo { get; set; } = ResultadoCobranca.Aprovada("SIM-00000000000000000000000000000001");

    public List<(Guid PedidoId, decimal Valor)> Chamadas { get; } = [];

    public Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken)
    {
        Chamadas.Add((pedidoId, valor));
        return Task.FromResult(Proximo);
    }
}
