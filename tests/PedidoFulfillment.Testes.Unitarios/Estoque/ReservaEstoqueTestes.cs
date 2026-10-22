using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Estoque;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;
using PedidoFulfillment.Testes.Unitarios.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Estoque;

/// <summary>Decisão do consumidor de estoque (REQ-08, research R-04; contracts/consumidor-estoque.md).</summary>
public sealed class ReservaEstoqueTestes
{
    private static readonly DateTimeOffset Criacao = new(2026, 10, 22, 15, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Agora = Criacao.AddSeconds(2);
    private static readonly Guid PedidoId = Guid.CreateVersion7();

    private readonly ArmazenamentoPedidosFalso armazenamento = new();
    private readonly LoggerFalso<ReservaEstoque> logger = new();
    private readonly Produto a;
    private readonly Produto b;

    public ReservaEstoqueTestes()
    {
        // Ids em ordem inversa à dos itens, para conferir a ordenação por produtoId no evento.
        b = NovoProduto("SKU-B", 5, Guid.Parse("0192b6a0-0000-7000-8000-000000000001"));
        a = NovoProduto("SKU-A", 10, Guid.Parse("0192b6a0-0000-7000-8000-000000000002"));
        armazenamento.Produtos.AddRange([a, b]);
    }

    private static Produto NovoProduto(string sku, int estoque, Guid id) => new(sku, $"Produto {sku}", null, 10m, estoque, true, Criacao, id);

    private ReservaEstoque Consumidor() => new(armazenamento, new RelogioFixo(Agora), logger);

    /// <summary>Guarda o pedido (3 de A e 2 de B) no status pedido; cada leitura monta uma instância nova.</summary>
    private void PedidoEm(StatusPedido status = StatusPedido.Pendente, bool reservado = false) =>
        armazenamento.GuardarPedido(PedidoId, () => Montar(status, reservado));

    private Pedido Montar(StatusPedido status, bool reservado)
    {
        var pedido = new Pedido(Guid.CreateVersion7(), "PED-20261022-000001", [(a, 3), (b, 2)], Criacao);
        typeof(Pedido).GetProperty(nameof(Pedido.Id))!.SetValue(pedido, PedidoId);
        typeof(Pedido).GetProperty(nameof(Pedido.EstoqueReservado))!.SetValue(pedido, reservado);
        StatusPedido[] caminho = status switch
        {
            StatusPedido.Pendente => [],
            StatusPedido.Processando => [StatusPedido.Processando],
            StatusPedido.ProntoParaEnvio => [StatusPedido.Processando, StatusPedido.ProntoParaEnvio],
            StatusPedido.Despachado => [StatusPedido.Processando, StatusPedido.ProntoParaEnvio, StatusPedido.Despachado],
            _ => [status]
        };
        foreach (var passo in caminho)
        {
            pedido.MudarStatus(passo, null, null, Criacao);
        }

        return pedido;
    }

    /// <summary>"pedido criado" com itens propositalmente diferentes dos gravados (FR-003).</summary>
    private static EnvelopeEvento<PedidoCriado> Evento(Guid pedidoId) => EnvelopeEvento.Criar(pedidoId, new PedidoCriado
    {
        PedidoId = pedidoId,
        Numero = "PED-20261022-000001",
        ClienteId = Guid.CreateVersion7(),
        Itens = [new ItemPedidoCriado { ProdutoId = Guid.CreateVersion7(), Quantidade = 99, PrecoUnitario = 1m }],
        ValorTotal = 99m,
        Moeda = "BRL"
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
    public void Consome_pedido_criado_na_fila_de_estoque()
    {
        var consumidor = Consumidor();

        consumidor.Consumidor.Should().Be("estoque");
        consumidor.Tipos.Should().Equal(TiposEvento.PedidoCriado);
    }

    [Fact]
    public async Task Pedido_com_estoque_e_reservado_sem_mudar_o_status()
    {
        PedidoEm();

        await Manipular();

        Estoque(a).Should().Be(7);
        Estoque(b).Should().Be(3);
        var pedido = Gravado();
        pedido.EstoqueReservado.Should().BeTrue();
        pedido.Status.Should().Be(StatusPedido.Pendente);
        pedido.Historico.Should().ContainSingle();
        armazenamento.Gravacoes.Should().Be(1);

        var envelope = EventoGravado<EstoqueReservado>(TiposEvento.EstoqueReservado);
        envelope.IdCorrelacao.Should().Be(PedidoId);
        envelope.OcorridoEm.Should().Be(Agora);
        envelope.Dados.PedidoId.Should().Be(PedidoId);
        envelope.Dados.Itens.Select(i => (i.ProdutoId, i.Quantidade)).Should().Equal((b.Id, 2), (a.Id, 3));
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 6001);
    }

    [Fact]
    public async Task Pedido_inexistente_e_falha_sem_gravar_nada()
    {
        var manipular = () => Manipular(Guid.CreateVersion7());

        await manipular.Should().ThrowAsync<InvalidOperationException>();
        armazenamento.Gravacoes.Should().Be(0);
        armazenamento.EventosGravados.Should().BeEmpty();
    }
}
