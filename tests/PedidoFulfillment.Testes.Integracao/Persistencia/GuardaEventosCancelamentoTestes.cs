using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>
/// A gravação recusa um pedido que vai a Cancelado sem <c>pedido.cancelado</c>, ou um pagamento que
/// vai a Estornado sem <c>pagamento.estornado</c> (REQ-15, R-03, FR-001 e FR-006).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class GuardaEventosCancelamentoTestes(BancoDadosTeste banco)
{
    private static readonly DateTimeOffset Cancelamento = DadosPersistencia.Instante.AddHours(1);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Grava pelo contexto, como os demais testes semeiam: pedido novo não passa pela guarda.</summary>
    private static async Task<Guid> SemearAsync(string stringConexao, Action<Pedido, Dictionary<Guid, Produto>>? preparar = null)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var clienteId = await DadosPersistencia.NovoUsuarioAsync(contexto);
        var produto = DadosPersistencia.NovoProduto(preco: 49.90m);
        var pedido = DadosPersistencia.NovoPedido(clienteId, produto);
        preparar?.Invoke(pedido, new Dictionary<Guid, Produto> { [produto.Id] = produto });
        contexto.Produtos.Add(produto);
        contexto.Pedidos.Add(pedido);
        await contexto.SaveChangesAsync(Token);
        return pedido.Id;
    }

    private static Task<int> EventosAsync(string stringConexao) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM EventosSaida");

    private static Task<string> StatusAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT Status FROM Pedidos WHERE Id = '{pedidoId}'");

    [Fact]
    public async Task Cancelamento_sem_pedido_cancelado_e_recusado_sem_gravar_nada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var pedidoId = await SemearAsync(stringConexao);
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var armazenamento = new ArmazenamentoPedidos(contexto);
        var pedido = (await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, Token))!;

        pedido.MudarStatus(StatusPedido.Cancelado, null, "Caminho que esqueceu o evento.", Cancelamento);
        var gravar = () => armazenamento.SalvarAsync(Token);

        (await gravar.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(TiposEvento.PedidoCancelado).And.Contain(pedido.Numero);
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("Pendente");
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM HistoricosStatusPedido WHERE PedidoId = '{pedidoId}'")).Should().Be(1);
        (await EventosAsync(stringConexao)).Should().Be(0);
    }

    [Fact]
    public async Task Cancelamento_com_o_pedido_cancelado_do_mesmo_pedido_e_gravado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var pedidoId = await SemearAsync(stringConexao);
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var armazenamento = new ArmazenamentoPedidos(contexto);
        var pedido = (await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, Token))!;

        var resultado = pedido.Cancelar(null, "Teste.", new Dictionary<Guid, Produto>(), Cancelamento);
        armazenamento.Adicionar(EventosPedido.Cancelado(pedido, MotivoCancelamento.Operacao, null, resultado, Cancelamento));
        await armazenamento.SalvarAsync(Token);

        (await StatusAsync(stringConexao, pedidoId)).Should().Be("Cancelado");
        (await EventosOutbox.CanceladoAsync(stringConexao, pedidoId)).Motivo.Should().Be(MotivoCancelamento.Operacao);
    }

    [Fact]
    public async Task Pedido_cancelado_de_outro_pedido_nao_vale()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var pedidoId = await SemearAsync(stringConexao);
        var outroId = await SemearAsync(stringConexao);
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var armazenamento = new ArmazenamentoPedidos(contexto);
        var pedido = (await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, Token))!;
        var outro = (await armazenamento.ObterPedidoAsync(outroId, paraAlterar: false, Token))!;

        var resultado = pedido.Cancelar(null, "Teste.", new Dictionary<Guid, Produto>(), Cancelamento);
        armazenamento.Adicionar(EventosPedido.Cancelado(outro, MotivoCancelamento.Operacao, null, resultado, Cancelamento));
        var gravar = () => armazenamento.SalvarAsync(Token);

        await gravar.Should().ThrowAsync<InvalidOperationException>();
        (await StatusAsync(stringConexao, pedidoId)).Should().Be("Pendente");
        (await EventosAsync(stringConexao)).Should().Be(0);
    }

    [Fact]
    public async Task Estorno_confirmado_sem_pagamento_estornado_e_recusado_e_com_o_evento_e_gravado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var pedidoId = await SemearAsync(stringConexao, (pedido, produtos) =>
        {
            pedido.ReservarEstoque(produtos, DadosPersistencia.Instante).Should().BeEmpty();
            pedido.AprovarPagamento("SIM-1", DadosPersistencia.Instante);
            pedido.Cancelar(null, "Teste.", produtos, DadosPersistencia.Instante);
        });

        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            var armazenamento = new ArmazenamentoPedidos(contexto);
            var pedido = (await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, Token))!;
            pedido.ConfirmarEstorno("SIM-EST-1", Cancelamento);
            var gravar = () => armazenamento.SalvarAsync(Token);

            (await gravar.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain(TiposEvento.PagamentoEstornado);
        }

        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT Situacao FROM Pagamentos WHERE PedidoId = '{pedidoId}'")).Should().Be("EstornoPendente");

        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            var armazenamento = new ArmazenamentoPedidos(contexto);
            var pedido = (await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, Token))!;
            pedido.ConfirmarEstorno("SIM-EST-1", Cancelamento);
            armazenamento.Adicionar(EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new PagamentoEstornado
            {
                PedidoId = pedido.Id,
                Numero = pedido.Numero,
                Valor = pedido.Pagamento!.Valor,
                IdentificadorTransacao = "SIM-1",
                IdentificadorEstorno = "SIM-EST-1",
                EstornadoEm = Cancelamento
            }, Cancelamento), Cancelamento));
            await armazenamento.SalvarAsync(Token);
        }

        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT Situacao FROM Pagamentos WHERE PedidoId = '{pedidoId}'")).Should().Be("Estornado");
    }

    [Fact]
    public async Task Pedido_novo_ja_cancelado_semeado_pelo_contexto_nao_passa_pela_guarda()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();

        var pedidoId = await SemearAsync(stringConexao, (pedido, _) => pedido.MudarStatus(StatusPedido.Cancelado, null, "Semeado.", DadosPersistencia.Instante));

        (await StatusAsync(stringConexao, pedidoId)).Should().Be("Cancelado");
        (await EventosAsync(stringConexao)).Should().Be(0);
    }
}
