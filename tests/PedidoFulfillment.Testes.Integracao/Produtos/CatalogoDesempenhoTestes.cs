using System.Diagnostics;
using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;
using static PedidoFulfillment.Testes.Integracao.Produtos.CenarioProdutos;

namespace PedidoFulfillment.Testes.Integracao.Produtos;

/// <summary>
/// SC-005 do REQ-22: com 10.000 produtos, a primeira página, uma página a 50 passos pelo cursor e
/// uma página com busca respondem em até 1 s cada.
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class CatalogoDesempenhoTestes(BancoDadosTeste banco)
{
    private const int Produtos = 10_000;
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PrazoPopulacao = TimeSpan.FromMinutes(2);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Primeira_pagina_pagina_funda_e_busca_respondem_em_ate_1_segundo()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await BancoDadosTeste.ExecutarAsync(stringConexao, $"""
            WITH n AS (
                SELECT TOP ({Produtos}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
                FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            INSERT INTO Produtos (Id, Sku, Nome, Descricao, Preco, QuantidadeEstoque, Ativo, CriadoEm, AtualizadoEm, TextoBusca)
            SELECT NEWID(), CONCAT('PERF-', FORMAT(i, '00000')),
                   CONCAT(CASE i % 4 WHEN 0 THEN N'Camiseta ' WHEN 1 THEN N'Caneca ' WHEN 2 THEN N'Mochila ' ELSE N'Garrafa ' END, FORMAT(i, '00000')),
                   N'Produto de carga', 10, 5, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(),
                   CONCAT(CASE i % 4 WHEN 0 THEN N'Camiseta ' WHEN 1 THEN N'Caneca ' WHEN 2 THEN N'Mochila ' ELSE N'Garrafa ' END,
                          FORMAT(i, '00000'), N' PERF-', FORMAT(i, '00000'), N' Produto de carga')
            FROM n;
            """);
        await EsperarPopulacaoAsync(stringConexao);

        await PaginaProdutosAsync(cliente);
        await PaginaProdutosAsync(cliente, Busca("camisetas"));

        var primeira = await MedirAsync(() => ListarProdutosAsync(cliente, "tamanho=20"));
        var cursor = ProximoProdutos(await PaginaProdutosAsync(cliente, "tamanho=20"));
        for (var i = 0; i < 49; i++)
        {
            cursor = ProximoProdutos(await PaginaProdutosAsync(cliente, $"tamanho=20&cursor={Uri.EscapeDataString(cursor!)}"));
        }

        var funda = await MedirAsync(() => ListarProdutosAsync(cliente, $"tamanho=20&cursor={Uri.EscapeDataString(cursor!)}"));
        var busca = await MedirAsync(() => ListarProdutosAsync(cliente, $"{Busca("camisetas carga")}&tamanho=20"));

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"primeira: {primeira.TotalMilliseconds:F0} ms; funda: {funda.TotalMilliseconds:F0} ms; busca: {busca.TotalMilliseconds:F0} ms.");
        primeira.Should().BeLessThan(Limite, "SC-005, primeira página");
        funda.Should().BeLessThan(Limite, "SC-005, página a 50 passos");
        busca.Should().BeLessThan(Limite, "SC-005, busca");
    }

    /// <summary>Espera o índice full-text terminar de processar a carga (PopulateStatus 0 = ocioso).</summary>
    private static async Task EsperarPopulacaoAsync(string stringConexao)
    {
        var cronometro = Stopwatch.StartNew();
        while (cronometro.Elapsed < PrazoPopulacao)
        {
            var pendentes = await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
                "SELECT CAST(OBJECTPROPERTYEX(OBJECT_ID('Produtos'), 'TableFulltextPendingChanges') AS int)");
            var estado = await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
                "SELECT CAST(FULLTEXTCATALOGPROPERTY('CatalogoProdutos', 'PopulateStatus') AS int)");
            if (pendentes == 0 && estado == 0)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), Cancelamento);
        }

        throw new TimeoutException("O índice full-text não terminou de processar a carga em 2 minutos.");
    }

    private static async Task<TimeSpan> MedirAsync(Func<Task<HttpResponseMessage>> operacao)
    {
        var cronometro = Stopwatch.StartNew();
        var resposta = await operacao();
        var duracao = cronometro.Elapsed;
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return duracao;
    }
}
