using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Estoque;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;
using static PedidoFulfillment.Testes.Integracao.Produtos.CenarioProdutos;

namespace PedidoFulfillment.Testes.Integracao.Produtos;

/// <summary>
/// REQ-22, User Story 3 e SC-002: a reposição pela API convive com as reservas do Worker ao mesmo
/// tempo, sem perder nem criar unidades. As reservas rodam como a base de consumo (REQ-08,
/// <see cref="DisputaEstoqueTestes"/>), em paralelo com o ajuste.
/// </summary>
public sealed class EstoqueConcorrenteTestes(BancoDadosTeste banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Reposicao_de_50_junto_de_30_reservas_termina_em_120_em_todas_as_rodadas()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        var (cliente, _, _) = await ClienteAsync(api);

        for (var rodada = 1; rodada <= 5; rodada++)
        {
            var (produtoId, pedidos) = await ProdutoComPedidosAsync(admin, cliente, rodada);

            // Como em produção: o Worker reserva um evento por vez, enquanto o Admin repõe em
            // paralelo, em cinco ajustes de 10 que disputam a mesma linha com as reservas.
            var worker = Task.Run(async () =>
            {
                foreach (var pedido in pedidos)
                {
                    await DisputaEstoqueTestes.ReservarComoABaseAsync(api.Services, pedido);
                }
            }, Cancelamento);
            var reposicoes = Enumerable.Range(0, 5).Select(_ => AjustarAsync(admin, produtoId, 10, "Reposição do fornecedor")).ToList();
            await worker;
            var respostas = await Task.WhenAll(reposicoes);

            respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK, $"rodada {rodada}");
            (await ReservadosAsync(stringConexao, pedidos)).Should().Be(30, $"rodada {rodada}");
            (await EstoqueAsync(stringConexao, produtoId)).Should().Be(120, $"rodada {rodada}: 100 + 50 - 30");
        }
    }

    /// <summary>
    /// Estresse acima do que o Worker faz: 30 reservas e a reposição, todas ao mesmo tempo. Quem
    /// esgota as tentativas desiste sem gravar nada (a reserva volta para a fila),
    /// o ajuste do Admin, atômico, sempre grava, e o estoque fecha exatamente com o que foi gravado: nenhuma unidade some nem aparece.
    /// </summary>
    [Fact]
    public async Task Sob_disputa_extrema_o_estoque_fecha_com_o_que_foi_gravado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        var (cliente, _, _) = await ClienteAsync(api);
        var (produtoId, pedidos) = await ProdutoComPedidosAsync(admin, cliente, 1);

        var reservas = pedidos.Select(async p =>
        {
            try
            {
                await DisputaEstoqueTestes.ReservarComoABaseAsync(api.Services, p);
            }
            catch (PedidoFulfillment.Application.Comum.ConflitoConcorrenciaException)
            {
                // Esgotou as tentativas da base: nada gravado; o Worker real tentaria de novo depois.
            }
        });
        var reposicao = AjustarAsync(admin, produtoId, 50, "Reposição do fornecedor");
        await Task.WhenAll(reservas);
        var resposta = await reposicao;

        resposta.StatusCode.Should().Be(HttpStatusCode.OK, "o ajuste é atômico e não precisa de nova tentativa");
        var reservados = await ReservadosAsync(stringConexao, pedidos);
        (await EstoqueAsync(stringConexao, produtoId)).Should().Be(150 - reservados);
    }

    private static async Task<(Guid ProdutoId, List<Guid> Pedidos)> ProdutoComPedidosAsync(HttpClient admin, HttpClient cliente, int rodada)
    {
        var produtoId = (await CadastradoAsync(admin, NovoSku("CONC"), $"Produto concorrente {rodada}", estoque: 100)).GetProperty("id").GetGuid();
        var pedidos = new List<Guid>();
        for (var i = 0; i < 30; i++)
        {
            pedidos.Add((await CriadoAsync(cliente, (produtoId, 1))).GetProperty("id").GetGuid());
        }

        return (produtoId, pedidos);
    }

    private static Task<int> ReservadosAsync(string stringConexao, List<Guid> pedidos) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM Pedidos WHERE Id IN ({string.Join(",", pedidos.Select(p => $"'{p}'"))}) AND EstoqueReservado = 1");

    [Fact]
    public async Task Baixa_que_deixaria_negativo_da_422_e_nada_muda()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        var produtoId = (await CadastradoAsync(admin, "BAIXA-1", "Produto", estoque: 2)).GetProperty("id").GetGuid();

        var resposta = await AjustarAsync(admin, produtoId, -3, "Avaria");

        resposta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodigoAsync(resposta)).Should().Be("ESTOQUE_INSUFICIENTE");
        (await EstoqueAsync(stringConexao, produtoId)).Should().Be(2);
    }

    [Fact]
    public async Task Devolucao_por_cancelamento_junto_de_um_ajuste_soma_as_duas()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        var (cliente, _, _) = await ClienteAsync(api);
        var produtoId = (await CadastradoAsync(admin, "DEV-1", "Produto", estoque: 10)).GetProperty("id").GetGuid();
        var pedidoId = (await CriadoAsync(cliente, (produtoId, 4))).GetProperty("id").GetGuid();
        await DisputaEstoqueTestes.ReservarComoABaseAsync(api.Services, pedidoId);
        (await EstoqueAsync(stringConexao, produtoId)).Should().Be(6);

        var cancelamento = MudarStatusAsync(cliente, pedidoId, "Cancelado", "Desisti.");
        var ajuste = AjustarAsync(admin, produtoId, 5, "Reposição");
        var respostas = await Task.WhenAll(cancelamento, ajuste);

        respostas.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        (await EstoqueAsync(stringConexao, produtoId)).Should().Be(15, "6 + 4 devolvidos + 5 repostos");
    }
}
