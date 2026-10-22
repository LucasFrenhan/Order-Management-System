using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Estoque;

/// <summary>
/// Consumidor de estoque pelo Worker, com SQL Server e RabbitMQ reais (REQ-08, US1, US2 e
/// US4; contracts/consumidor-estoque.md).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class ReservaEstoqueTestes(MensageriaTeste mensageria)
{
    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Pedido_com_estoque_e_reservado_e_anunciado_em_ate_5_segundos()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync("estoque.*");
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao);

        var criado = await CriadoAsync(cliente, (Cat001, 2), (Cat002, 1));
        var pedidoId = criado.GetProperty("id").GetGuid();

        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(DoPedido(fila, pedidoId).Count > 0), Prazo)).Should().BeTrue();
        var mensagem = DoPedido(fila, pedidoId).Should().ContainSingle().Subject;
        mensagem.Propriedades.Type.Should().Be(TiposEvento.EstoqueReservado);
        var envelope = SerializacaoEventos.Desserializar<EstoqueReservado>(mensagem.Texto);
        envelope.IdCorrelacao.Should().Be(pedidoId);
        envelope.Dados.Itens.Select(i => (i.ProdutoId, i.Quantidade)).Should().Equal((Cat001, 2), (Cat002, 1));
        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(48);
        (await EstoqueAsync(stringConexao, Cat002)).Should().Be(99);
        (await ReservadoAsync(stringConexao, pedidoId)).Should().BeTrue();
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("Pendente");
    }

    [Theory]
    [InlineData("UPDATE Produtos SET QuantidadeEstoque = 1 WHERE Sku = 'CAT-002'", 1, false)]
    [InlineData("UPDATE Produtos SET Ativo = 0 WHERE Sku = 'CAT-002'", 100, true)]
    public async Task Item_que_ficou_indisponivel_antes_do_consumo_cancela_o_pedido(string alteracao, int disponivel, bool inativo)
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync("estoque.*");
        using var logs = new CapturaLogs();
        await DeclararFilaEstoqueAsync(mensageria);

        // O Worker sobe depois: a falta acontece entre a criação e o consumo.
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2), (Cat002, 3))).GetProperty("id").GetGuid();
        await BancoDadosTeste.ExecutarAsync(stringConexao, alteracao);
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, logs);

        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(DoPedido(fila, pedidoId).Count > 0), Prazo)).Should().BeTrue();
        var mensagem = DoPedido(fila, pedidoId).Should().ContainSingle().Subject;
        mensagem.Propriedades.Type.Should().Be(TiposEvento.EstoqueInsuficiente);
        var item = SerializacaoEventos.Desserializar<EstoqueInsuficiente>(mensagem.Texto).Dados.Itens.Should().ContainSingle().Subject;
        (item.ProdutoId, item.QuantidadeSolicitada, item.QuantidadeDisponivel, item.Inativo).Should().Be((Cat002, 3, disponivel, inativo));
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("Cancelado");
        (await ReservadoAsync(stringConexao, pedidoId)).Should().BeFalse();
        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(50);
        (await EstoqueAsync(stringConexao, Cat002)).Should().Be(disponivel);
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao,
            $"SELECT TOP 1 Motivo FROM HistoricosStatusPedido WHERE PedidoId = '{pedidoId}' ORDER BY OcorridoEm DESC, Id DESC")).Should().Be("Estoque insuficiente.");

        // Reentrega do mesmo evento depois do cancelamento: nada muda (cenário 4).
        var original = await PedidoCriadoGravadoAsync(stringConexao, pedidoId);
        await PublicarAsync(mensageria, original, SerializacaoEventos.Desserializar<PedidoCriado>(original).IdEvento.ToString());
        (await EsperarLogAsync(logs, 5005, Prazo)).Should().BeTrue();
        DoPedido(fila, pedidoId).Should().ContainSingle();
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("Cancelado");
    }

    [Fact]
    public async Task Reentrega_do_mesmo_pedido_criado_nao_reserva_de_novo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync("estoque.*");
        using var logs = new CapturaLogs();
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, logs);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2))).GetProperty("id").GetGuid();
        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(DoPedido(fila, pedidoId).Count > 0), Prazo)).Should().BeTrue();

        var original = await PedidoCriadoGravadoAsync(stringConexao, pedidoId);
        await PublicarAsync(mensageria, original, SerializacaoEventos.Desserializar<PedidoCriado>(original).IdEvento.ToString());

        (await EsperarLogAsync(logs, 5005, Prazo)).Should().BeTrue("a base descarta o duplicado antes do manipulador");
        DoPedido(fila, pedidoId).Should().ContainSingle();
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM EventosSaida WHERE Tipo LIKE 'estoque.%'")).Should().Be(1);
        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(48);
    }
}
