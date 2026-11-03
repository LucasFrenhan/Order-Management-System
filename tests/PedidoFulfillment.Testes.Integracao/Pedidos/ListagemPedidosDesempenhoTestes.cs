using System.Diagnostics;
using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioListagem;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>
/// SC-004 do REQ-21: com 10.000 pedidos, a primeira página, uma página funda (50 passos pelo
/// cursor) e uma página filtrada respondem em até 1 s cada, sem piorar com a profundidade.
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class ListagemPedidosDesempenhoTestes(BancoDadosTeste banco)
{
    private const int Pedidos = 10_000;
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task Primeira_pagina_pagina_funda_e_pagina_filtrada_respondem_em_ate_1_segundo()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (dono, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var (modelo, _) = await PedidoAsync(dono);
        await InserirEmLoteAsync(stringConexao, modelo);

        // Aquecimento: plano de execução e conexões.
        await PaginaAsync(dono);
        await PaginaAsync(operador);

        var primeira = await MedirAsync(() => ListarAsync(dono, "tamanho=20"));
        var cursor = Proximo(await PaginaAsync(operador, "tamanho=20"));
        for (var i = 0; i < 49; i++)
        {
            cursor = Proximo(await PaginaAsync(operador, $"tamanho=20&{Cursor(cursor)}"));
        }

        var funda = await MedirAsync(() => ListarAsync(operador, $"tamanho=20&{Cursor(cursor)}"));
        var filtrada = await MedirAsync(() => ListarAsync(operador,
            $"status=Processando&criadoDe={Data(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))}&tamanho=20"));

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"primeira: {primeira.TotalMilliseconds:F0} ms; funda: {funda.TotalMilliseconds:F0} ms; filtrada: {filtrada.TotalMilliseconds:F0} ms.");
        primeira.Should().BeLessThan(Limite, "SC-004, primeira página");
        funda.Should().BeLessThan(Limite, "SC-004, página a 50 passos");
        filtrada.Should().BeLessThan(Limite, "SC-004, página filtrada");
    }

    private static async Task<TimeSpan> MedirAsync(Func<Task<HttpResponseMessage>> operacao)
    {
        var cronometro = Stopwatch.StartNew();
        var resposta = await operacao();
        var duracao = cronometro.Elapsed;
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return duracao;
    }

    /// <summary>Copia o pedido modelo 10.000 vezes, com um item cada, em um só comando.</summary>
    private static Task InserirEmLoteAsync(string stringConexao, Guid modelo) =>
        BancoDadosTeste.ExecutarAsync(stringConexao, $"""
            WITH n AS (
                SELECT TOP ({Pedidos}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
                FROM sys.all_objects a CROSS JOIN sys.all_objects b)
            SELECT NEWID() AS Id, n.i INTO #Novos FROM n;

            INSERT INTO Pedidos (Id, Numero, ClienteId, Status, Moeda, ValorTotal, CriadoEm, AtualizadoEm)
            SELECT x.Id, CONCAT('PED-20260101-', FORMAT(500000 + x.i, '000000')), p.ClienteId,
                   CASE x.i % 3 WHEN 0 THEN 'Processando' WHEN 1 THEN 'Pendente' ELSE 'Cancelado' END,
                   p.Moeda, p.ValorTotal, DATEADD(MINUTE, x.i, CAST('2026-01-01T00:00:00+00:00' AS datetimeoffset)), DATEADD(MINUTE, x.i, CAST('2026-01-01T00:00:00+00:00' AS datetimeoffset))
            FROM #Novos x CROSS JOIN Pedidos p WHERE p.Id = '{modelo}';

            INSERT INTO ItensPedido (PedidoId, ProdutoId, NomeProduto, Quantidade, PrecoUnitario, Subtotal)
            SELECT x.Id, i.ProdutoId, i.NomeProduto, i.Quantidade, i.PrecoUnitario, i.Subtotal
            FROM #Novos x CROSS JOIN ItensPedido i WHERE i.PedidoId = '{modelo}';
            """);
}
