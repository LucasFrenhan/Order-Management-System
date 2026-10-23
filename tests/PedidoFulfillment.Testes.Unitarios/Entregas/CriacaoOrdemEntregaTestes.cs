using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Entregas;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;
using PedidoFulfillment.Testes.Unitarios.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Entregas;

/// <summary>Decisão do consumidor de entrega (REQ-10, research R-04; contracts/consumidor-entrega.md).</summary>
public sealed class CriacaoOrdemEntregaTestes
{
    private const string Numero = "PED-20261023-000001";
    private const string Transacao = "SIM-0199f6b12c3d7e4f8a5b6c7d8e9f0a12";
    private static readonly DateTimeOffset Criacao = new(2026, 10, 23, 15, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Agora = Criacao.AddSeconds(2);
    private static readonly Guid PedidoId = Guid.CreateVersion7();
    private static readonly StatusPedido[] Fluxo = [StatusPedido.Pendente, StatusPedido.Processando, StatusPedido.ProntoParaEnvio, StatusPedido.Despachado];

    private readonly ArmazenamentoPedidosFalso armazenamento = new();
    private readonly LoggerFalso<CriacaoOrdemEntrega> logger = new();
    private readonly Produto a = new("SKU-A", "Produto A", null, 10m, 7, true, Criacao, Guid.Parse("0192b6a0-0000-7000-8000-000000000002"));
    private readonly Produto b = new("SKU-B", "Produto B", null, 12.5m, 3, true, Criacao, Guid.Parse("0192b6a0-0000-7000-8000-000000000001"));

    public CriacaoOrdemEntregaTestes() => armazenamento.Produtos.AddRange([a, b]);

    private CriacaoOrdemEntrega Consumidor() => new(armazenamento, new RelogioFixo(Agora), logger);

    /// <summary>Guarda o pedido (3 de A e 2 de B), pago e reservado por padrão; cada leitura monta uma instância nova.</summary>
    private void PedidoEm(StatusPedido status = StatusPedido.Processando, bool reservado = true, bool pago = true, bool comOrdem = false) =>
        armazenamento.GuardarPedido(PedidoId, () => Montar(status, reservado, pago, comOrdem));

    private Pedido Montar(StatusPedido status, bool reservado, bool pago, bool comOrdem)
    {
        var pedido = new Pedido(Guid.CreateVersion7(), Numero, [(a, 3), (b, 2)], Criacao);
        typeof(Pedido).GetProperty(nameof(Pedido.Id))!.SetValue(pedido, PedidoId);
        typeof(Pedido).GetProperty(nameof(Pedido.EstoqueReservado))!.SetValue(pedido, true);
        if (pago)
        {
            pedido.AprovarPagamento(Transacao, Criacao);
        }

        if (comOrdem)
        {
            pedido.CriarOrdemEntrega(Criacao);
        }

        var atual = Array.IndexOf(Fluxo, pedido.Status);
        var alvo = Array.IndexOf(Fluxo, status);
        StatusPedido[] caminho = alvo >= 0 ? Fluxo[(atual + 1)..(alvo + 1)] : [status];
        foreach (var passo in caminho)
        {
            pedido.MudarStatus(passo, Guid.CreateVersion7(), null, Criacao);
        }

        typeof(Pedido).GetProperty(nameof(Pedido.EstoqueReservado))!.SetValue(pedido, reservado);
        return pedido;
    }

    private static EnvelopeEvento<PagamentoAprovado> Evento(Guid pedidoId) => EnvelopeEvento.Criar(pedidoId, new PagamentoAprovado
    {
        PedidoId = pedidoId,
        Valor = 55m,
        IdentificadorTransacao = Transacao
    }, Criacao);

    private Task Manipular(Guid? pedidoId = null) => Consumidor().ManipularAsync(Evento(pedidoId ?? PedidoId), CancellationToken.None);

    private Pedido Gravado() => armazenamento.ObterPedidoAsync(PedidoId, false, CancellationToken.None).Result!;

    private EnvelopeEvento<EntregaCriada> EntregaCriadaGravada()
    {
        var evento = armazenamento.EventosGravados.Should().ContainSingle().Subject;
        evento.Tipo.Should().Be(TiposEvento.EntregaCriada);
        return SerializacaoEventos.Desserializar<EntregaCriada>(evento.Conteudo);
    }

    [Fact]
    public void Consome_pagamento_aprovado_na_fila_de_entrega()
    {
        var consumidor = Consumidor();

        consumidor.Consumidor.Should().Be("entrega");
        consumidor.Tipos.Should().Equal(TiposEvento.PagamentoAprovado);
    }

    [Fact]
    public async Task Pedido_em_processando_ganha_a_ordem_vai_para_pronto_para_envio_e_publica_entrega_criada()
    {
        PedidoEm();

        await Manipular();

        var pedido = Gravado();
        var ordem = pedido.OrdemEntrega!;
        ordem.Should().NotBeNull();
        (ordem.CriadoEm, ordem.Transportadora, ordem.CodigoRastreio, ordem.DespachadoEm).Should().Be((Agora, (string?)null, (string?)null, (DateTimeOffset?)null));
        pedido.Status.Should().Be(StatusPedido.ProntoParaEnvio);
        var entrada = pedido.Historico.Last();
        (entrada.StatusAnterior, entrada.StatusNovo, entrada.AutorId, entrada.Motivo, entrada.OcorridoEm)
            .Should().Be((StatusPedido.Processando, StatusPedido.ProntoParaEnvio, (Guid?)null, Pedido.MotivoOrdemEntregaCriada, Agora));
        pedido.EstoqueReservado.Should().BeTrue();
        armazenamento.LeiturasProdutosParaAlterar.Should().Be(0, "o fulfillment não mexe em estoque (FR-006)");
        armazenamento.Gravacoes.Should().Be(1);

        var envelope = EntregaCriadaGravada();
        envelope.IdCorrelacao.Should().Be(PedidoId);
        envelope.OcorridoEm.Should().Be(Agora);
        (envelope.Dados.PedidoId, envelope.Dados.Numero, envelope.Dados.OrdemEntregaId).Should().Be((PedidoId, Numero, ordem.Id));
        envelope.Dados.Itens.Select(i => (i.ProdutoId, i.Quantidade)).Should().Equal((a.Id, 3), (b.Id, 2));
        var log = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 8001).Subject;
        log.Propriedades["OrdemEntregaId"].Should().Be(ordem.Id);
        log.Propriedades["StatusMudou"].Should().Be(true);
    }

    [Fact]
    public async Task Pedido_inexistente_falha_sem_gravar()
    {
        var id = Guid.CreateVersion7();

        await FluentActions.Awaiting(() => Manipular(id)).Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{id}*");

        armazenamento.Gravacoes.Should().Be(0);
        armazenamento.EventosGravados.Should().BeEmpty();
    }
}
