using System.Net;
using FluentAssertions;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Estoque.DisputaEstoqueTestes;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Estoque;

/// <summary>
/// Devolução do estoque no cancelamento pela API, com SQL Server e RabbitMQ reais (REQ-08,
/// US5, FR-010 e FR-011; research R-05). O contrato HTTP v1 não muda.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class DevolucaoEstoqueTestes(MensageriaTeste mensageria)
{
    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(5);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static async Task<Guid> NovoProdutoAsync(string stringConexao, int estoque)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var produto = new Produto($"DEV-{Guid.NewGuid():N}"[..20], "Produto devolvido", null, 10m, estoque, true, DateTimeOffset.UtcNow);
        contexto.Produtos.Add(produto);
        await contexto.SaveChangesAsync(Cancelamento);
        return produto.Id;
    }

    [Fact]
    public async Task Cliente_cancela_pedido_reservado_pelo_worker_e_o_estoque_volta()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2), (Cat002, 1))).GetProperty("id").GetGuid();
        (await MensageriaTeste.EsperarAteAsync(() => ReservadoAsync(stringConexao, pedidoId), Prazo)).Should().BeTrue();
        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(48);

        var resposta = await MudarStatusAsync(cliente, pedidoId, "Cancelado", "Desisti");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CorpoAsync(resposta)).GetProperty("status").GetString().Should().Be("Cancelado");
        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(50);
        (await EstoqueAsync(stringConexao, Cat002)).Should().Be(100);
        (await ReservadoAsync(stringConexao, pedidoId)).Should().BeFalse();
    }

    [Fact]
    public async Task Operacao_cancela_pedido_reservado_em_processando_e_o_estoque_volta()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var admin = await AdminAsync(api);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 3))).GetProperty("id").GetGuid();
        await ReservarComoABaseAsync(api.Services, pedidoId);
        (await MudarStatusAsync(admin, pedidoId, "Processando")).EnsureSuccessStatusCode();

        (await MudarStatusAsync(admin, pedidoId, "Cancelado", "Pagamento não confirmado")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(50);
        (await ReservadoAsync(stringConexao, pedidoId)).Should().BeFalse();
    }

    [Fact]
    public async Task Cancelado_antes_da_reserva_nao_mexe_no_estoque()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync("estoque.*");
        using var logs = new CapturaLogs();
        await DeclararFilaEstoqueAsync(mensageria);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2))).GetProperty("id").GetGuid();

        (await MudarStatusAsync(cliente, pedidoId, "Cancelado", "Desisti")).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, logs);

        (await EsperarLogAsync(logs, 6003, Prazo)).Should().BeTrue();
        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(50);
        DoPedido(fila, pedidoId).Should().BeEmpty();
    }

    [Fact]
    public async Task Cancelamento_e_reserva_simultaneos_nunca_prendem_nem_inventam_estoque()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);

        for (var rodada = 1; rodada <= 10; rodada++)
        {
            var produtoId = await NovoProdutoAsync(stringConexao, 1);
            var pedidoId = (await CriadoAsync(cliente, (produtoId, 1))).GetProperty("id").GetGuid();

            var cancelamento = MudarStatusAsync(cliente, pedidoId, "Cancelado", "Desisti");
            var reserva = ReservarComoABaseAsync(api.Services, pedidoId);
            await Task.WhenAll(cancelamento, reserva);

            (await cancelamento).StatusCode.Should().Be(HttpStatusCode.OK, $"rodada {rodada}");
            (await StatusAsync(stringConexao, pedidoId)).Should().Be("Cancelado", $"rodada {rodada}");
            (await ReservadoAsync(stringConexao, pedidoId)).Should().BeFalse($"rodada {rodada}");
            (await EstoqueAsync(stringConexao, produtoId)).Should().Be(1, $"rodada {rodada}");
        }
    }

    [Fact]
    public async Task Produto_desativado_depois_da_reserva_recebe_o_estoque_de_volta()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 2))).GetProperty("id").GetGuid();
        await ReservarComoABaseAsync(api.Services, pedidoId);
        await BancoDadosTeste.ExecutarAsync(stringConexao, $"UPDATE Produtos SET Ativo = 0 WHERE Id = '{Cat001}'");

        (await MudarStatusAsync(cliente, pedidoId, "Cancelado", "Desisti")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(50);
        (await BancoDadosTeste.ConsultarAsync<bool>(stringConexao, $"SELECT Ativo FROM Produtos WHERE Id = '{Cat001}'")).Should().BeFalse();
    }
}
