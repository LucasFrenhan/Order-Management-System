using FluentAssertions;
using PedidoFulfillment.Application.Comum;
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

    [Fact]
    public async Task AplicarAsync_devolve_verdadeiro_quando_grava_e_falso_quando_o_pedido_nao_esta_apto()
    {
        // A regra que a reconciliação reaproveita (REQ-12, R-04), sem a mensagem.
        PedidoEm();
        (await Consumidor().AplicarAsync(Guid.Empty, PedidoId, CancellationToken.None)).Should().BeTrue();
        armazenamento.Gravacoes.Should().Be(1);

        PedidoEm(reservado: true);
        (await Consumidor().AplicarAsync(Guid.Empty, PedidoId, CancellationToken.None)).Should().BeFalse();
        armazenamento.Gravacoes.Should().Be(1);
    }

    private Pedido Gravado() => armazenamento.ObterPedidoAsync(PedidoId, false, CancellationToken.None).Result!;

    private int Estoque(Produto produto) => armazenamento.Produtos.Single(p => p.Id == produto.Id).QuantidadeEstoque;

    /// <summary>O único evento do tipo; a falta de estoque grava também o <c>pedido.cancelado</c> (REQ-15).</summary>
    private EnvelopeEvento<T> EventoGravado<T>(string tipo)
    {
        var evento = armazenamento.EventosGravados.Should().ContainSingle(e => e.Tipo == tipo).Subject;
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
    public async Task Item_sem_estoque_suficiente_cancela_o_pedido_sem_mexer_no_estoque()
    {
        armazenamento.AlterarProduto(b.Id, p => p.AjustarEstoque(-4, Criacao));
        PedidoEm();

        await Manipular();

        Estoque(a).Should().Be(10);
        Estoque(b).Should().Be(1);
        var pedido = Gravado();
        pedido.Status.Should().Be(StatusPedido.Cancelado);
        pedido.EstoqueReservado.Should().BeFalse();
        var entrada = pedido.Historico.Last();
        (entrada.StatusAnterior, entrada.StatusNovo, entrada.AutorId, entrada.Motivo, entrada.OcorridoEm)
            .Should().Be((StatusPedido.Pendente, StatusPedido.Cancelado, (Guid?)null, "Estoque insuficiente.", Agora));

        var envelope = EventoGravado<EstoqueInsuficiente>(TiposEvento.EstoqueInsuficiente);
        envelope.IdCorrelacao.Should().Be(PedidoId);
        envelope.Dados.PedidoId.Should().Be(PedidoId);
        var item = envelope.Dados.Itens.Should().ContainSingle("só os itens que faltaram").Subject;
        (item.ProdutoId, item.QuantidadeSolicitada, item.QuantidadeDisponivel, item.Inativo).Should().Be((b.Id, 2, 1, false));
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 6002);
    }

    [Fact]
    public async Task Falta_de_estoque_grava_tambem_o_pedido_cancelado_na_mesma_gravacao()
    {
        armazenamento.AlterarProduto(b.Id, p => p.AjustarEstoque(-4, Criacao));
        PedidoEm();

        await Manipular();

        // REQ-15, US1 cenário 4: os dois eventos saem juntos, numa gravação só.
        armazenamento.Gravacoes.Should().Be(1);
        armazenamento.EventosGravados.Select(e => e.Tipo).Should().BeEquivalentTo([TiposEvento.EstoqueInsuficiente, TiposEvento.PedidoCancelado]);
        var cancelado = EventoGravado<PedidoCancelado>(TiposEvento.PedidoCancelado);
        cancelado.IdCorrelacao.Should().Be(PedidoId);
        cancelado.Dados.Motivo.Should().Be(MotivoCancelamento.EstoqueInsuficiente);
        cancelado.Dados.EtapaEsgotada.Should().BeNull();
        cancelado.Dados.CanceladoEm.Should().Be(Agora);
        (cancelado.Dados.EstoqueDevolvido, cancelado.Dados.EstornoPendente, cancelado.Dados.OrdemEntregaCancelada).Should().Be((false, false, false));
    }

    [Fact]
    public async Task Reserva_bem_sucedida_nao_grava_pedido_cancelado()
    {
        PedidoEm();

        await Manipular();

        armazenamento.EventosGravados.Should().NotContain(e => e.Tipo == TiposEvento.PedidoCancelado);
    }

    [Fact]
    public async Task Produto_desativado_depois_da_criacao_conta_como_falta()
    {
        armazenamento.AlterarProduto(a.Id, p => p.Desativar(Criacao));
        PedidoEm();

        await Manipular();

        Gravado().Status.Should().Be(StatusPedido.Cancelado);
        Estoque(a).Should().Be(10);
        Estoque(b).Should().Be(5);
        var item = EventoGravado<EstoqueInsuficiente>(TiposEvento.EstoqueInsuficiente).Dados.Itens.Should().ContainSingle().Subject;
        (item.ProdutoId, item.QuantidadeSolicitada, item.QuantidadeDisponivel, item.Inativo).Should().Be((a.Id, 3, 10, true));
    }

    [Fact]
    public async Task Faltas_saem_ordenadas_por_produto()
    {
        armazenamento.AlterarProduto(a.Id, p => p.Desativar(Criacao));
        armazenamento.AlterarProduto(b.Id, p => p.AjustarEstoque(-5, Criacao));
        PedidoEm();

        await Manipular();

        EventoGravado<EstoqueInsuficiente>(TiposEvento.EstoqueInsuficiente).Dados.Itens.Select(i => i.ProdutoId).Should().Equal(b.Id, a.Id);
    }

    [Fact]
    public async Task Pedido_movido_para_processando_sem_estoque_tambem_e_cancelado()
    {
        armazenamento.AlterarProduto(b.Id, p => p.AjustarEstoque(-5, Criacao));
        PedidoEm(StatusPedido.Processando);

        await Manipular();

        var pedido = Gravado();
        pedido.Status.Should().Be(StatusPedido.Cancelado);
        pedido.Historico.Last().StatusAnterior.Should().Be(StatusPedido.Processando);
        EventoGravado<EstoqueInsuficiente>(TiposEvento.EstoqueInsuficiente);
    }

    [Fact]
    public async Task Conflito_seguido_de_sucesso_rele_e_reserva_uma_vez()
    {
        PedidoEm();
        armazenamento.ConflitosRestantes = 1;

        await Manipular();

        armazenamento.LeiturasProdutosParaAlterar.Should().Be(2);
        armazenamento.Descartes.Should().Be(1);
        armazenamento.Gravacoes.Should().Be(1);
        Estoque(a).Should().Be(7);
        Estoque(b).Should().Be(3);
        EventoGravado<EstoqueReservado>(TiposEvento.EstoqueReservado);
        var conflito = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 6004).Subject;
        conflito.Nivel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        conflito.Propriedades["Reavaliacao"].Should().Be(1);
    }

    [Fact]
    public async Task Conflito_reavaliado_com_o_estoque_atual_pode_virar_cancelamento()
    {
        PedidoEm();
        armazenamento.ConflitosRestantes = 1;
        // A outra operação levou o estoque de B enquanto esta reserva gravava.
        armazenamento.AoConflitar = () => armazenamento.AlterarProduto(b.Id, p => p.AjustarEstoque(-5, Criacao));

        await Manipular();

        Gravado().Status.Should().Be(StatusPedido.Cancelado);
        Estoque(a).Should().Be(10);
        Estoque(b).Should().Be(0);
        var item = EventoGravado<EstoqueInsuficiente>(TiposEvento.EstoqueInsuficiente).Dados.Itens.Should().ContainSingle().Subject;
        (item.ProdutoId, item.QuantidadeDisponivel).Should().Be((b.Id, 0));
    }

    [Fact]
    public async Task Conflito_nas_tres_reavaliacoes_relanca_sem_deixar_nada_pendente()
    {
        PedidoEm();
        armazenamento.ConflitosRestantes = 3;

        var manipular = () => Manipular();

        await manipular.Should().ThrowAsync<ConflitoConcorrenciaException>();
        armazenamento.Gravacoes.Should().Be(0);
        armazenamento.EventosGravados.Should().BeEmpty();
        armazenamento.EventosAdicionados.Should().BeEmpty();
        Estoque(a).Should().Be(10);
        logger.Eventos.Where(e => e.Id.Id == 6004).Select(e => e.Propriedades["Reavaliacao"]).Should().Equal(1, 2, 3);
    }

    [Theory]
    [InlineData(StatusPedido.Cancelado)]
    [InlineData(StatusPedido.Despachado)]
    [InlineData(StatusPedido.PagamentoRejeitado)]
    public async Task Pedido_em_status_final_e_ignorado(StatusPedido status)
    {
        PedidoEm(status);

        await Manipular();

        armazenamento.Gravacoes.Should().Be(0);
        armazenamento.LeiturasProdutosParaAlterar.Should().Be(0);
        armazenamento.EventosGravados.Should().BeEmpty();
        Estoque(a).Should().Be(10);
        Gravado().Status.Should().Be(status);
        var ignorado = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 6003).Subject;
        ignorado.Propriedades["Status"].Should().Be(status);
        ignorado.Propriedades["JaReservado"].Should().Be(false);
    }

    [Fact]
    public async Task Pedido_ja_reservado_e_ignorado()
    {
        PedidoEm(reservado: true);

        await Manipular();

        armazenamento.Gravacoes.Should().Be(0);
        armazenamento.EventosGravados.Should().BeEmpty();
        Estoque(a).Should().Be(10);
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 6003).Subject.Propriedades["JaReservado"].Should().Be(true);
    }

    [Theory]
    [InlineData(StatusPedido.Processando)]
    [InlineData(StatusPedido.ProntoParaEnvio)]
    public async Task Pedido_movido_pela_operacao_antes_da_reserva_e_reservado(StatusPedido status)
    {
        PedidoEm(status);

        await Manipular();

        var pedido = Gravado();
        pedido.Status.Should().Be(status);
        pedido.EstoqueReservado.Should().BeTrue();
        Estoque(a).Should().Be(7);
        EventoGravado<EstoqueReservado>(TiposEvento.EstoqueReservado);
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
