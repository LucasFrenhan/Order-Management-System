using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>
/// SC-005 e FR-010: com 10.000 pedidos, as consultas por cliente, por número e por status
/// respondem em menos de 100 ms (mediana de 20 execuções após aquecimento, research R-10).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class DesempenhoConsultaTestes(BancoDadosTeste banco)
{
    private const int Usuarios = 100;
    private const int Pedidos = 10_000;
    private const int Aquecimento = 3;
    private const int Medicoes = 20;
    private static readonly TimeSpan Limite = TimeSpan.FromMilliseconds(100);

    // 10.000 pedidos em um comando; 10% em Processando, distribuídos entre os usuários.
    private const string CarregarPedidos = """
        WITH n AS (
            SELECT TOP (10000) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS i
            FROM sys.all_objects a CROSS JOIN sys.all_objects b),
        u AS (SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) - 1 AS k FROM Usuarios)
        INSERT INTO Pedidos (Id, Numero, ClienteId, Status, ValorTotal, Moeda, CriadoEm, AtualizadoEm)
        SELECT NEWID(), CONCAT('PED-20261022-', RIGHT(CONCAT('000000', n.i), 6)), u.Id,
               CASE WHEN n.i % 10 = 0 THEN N'Processando' ELSE N'Pendente' END, 10, N'BRL',
               DATEADD(SECOND, n.i, CAST('2026-10-22T00:00:00+00:00' AS datetimeoffset)),
               DATEADD(SECOND, n.i, CAST('2026-10-22T00:00:00+00:00' AS datetimeoffset))
        FROM n JOIN u ON u.k = n.i % 100;
        """;

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Consultas_principais_respondem_em_menos_de_100_ms_com_10_mil_pedidos()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        Guid clienteId;
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            clienteId = await DadosPersistencia.NovoUsuarioAsync(contexto);
            for (var i = 1; i < Usuarios; i++)
            {
                await DadosPersistencia.NovoUsuarioAsync(contexto);
            }
        }

        (await BancoDadosTeste.ExecutarAsync(stringConexao, CarregarPedidos)).Should().Be(Pedidos);
        await BancoDadosTeste.ExecutarAsync(stringConexao, "UPDATE STATISTICS Pedidos");
        const string numero = "PED-20261022-005000";

        await using var leitura = BancoDadosTeste.NovoContexto(stringConexao);
        var porCliente = await MedianaAsync(() => leitura.Pedidos.AsNoTracking()
            .Where(p => p.ClienteId == clienteId).OrderByDescending(p => p.CriadoEm).ToListAsync(Cancelamento));
        var porNumero = await MedianaAsync(() => leitura.Pedidos.AsNoTracking().SingleAsync(p => p.Numero == numero, Cancelamento));
        var porStatus = await MedianaAsync(() => leitura.Pedidos.AsNoTracking()
            .Where(p => p.Status == StatusPedido.Processando).Take(50).ToListAsync(Cancelamento));

        porCliente.Should().BeLessThan(Limite);
        porNumero.Should().BeLessThan(Limite);
        porStatus.Should().BeLessThan(Limite);
    }

    [Fact]
    public async Task Os_indices_de_consulta_existem()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();

        var indices = await BancoDadosTeste.ConsultarAsync<string>(stringConexao,
            "SELECT STRING_AGG(name, ',') WITHIN GROUP (ORDER BY name) FROM sys.indexes WHERE name IN " +
            "('IX_Pedidos_ClienteId_CriadoEm', 'IX_Pedidos_Status', 'IX_HistoricosStatusPedido_PedidoId_Id', 'IX_ItensPedido_ProdutoId')");

        indices.Should().Be("IX_HistoricosStatusPedido_PedidoId_Id,IX_ItensPedido_ProdutoId,IX_Pedidos_ClienteId_CriadoEm,IX_Pedidos_Status");
    }

    private static async Task<TimeSpan> MedianaAsync<T>(Func<Task<T>> consulta)
    {
        for (var i = 0; i < Aquecimento; i++)
        {
            await consulta();
        }

        var tempos = new List<TimeSpan>();
        for (var i = 0; i < Medicoes; i++)
        {
            var cronometro = Stopwatch.StartNew();
            await consulta();
            tempos.Add(cronometro.Elapsed);
        }

        tempos.Sort();
        return tempos[Medicoes / 2];
    }
}
