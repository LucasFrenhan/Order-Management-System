using System.Diagnostics;
using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>
/// SC-004 do REQ-11: 95% das consultas e dos rastreios em menos de 500 ms no ambiente local, com
/// um pedido de 10 itens e 10 entradas de histórico.
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class DesempenhoRastreioTestes(BancoDadosTeste banco)
{
    private const int Aquecimento = 5;
    private const int Medicoes = 40;
    private static readonly TimeSpan Limite = TimeSpan.FromMilliseconds(500);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Noventa_e_cinco_por_cento_das_consultas_e_dos_rastreios_levam_menos_de_500_ms()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var pedidoId = await PedidoProntoAsync(stringConexao, cliente, operador);
        var rota = $"{Rota}/{pedidoId}";

        for (var i = 0; i < Aquecimento; i++)
        {
            (await cliente.GetAsync(rota, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var consultas = await MedirAsync(() => cliente.GetAsync(rota, Cancelamento));
        // O primeiro despacha; os seguintes corrigem, alternando dois códigos.
        var rastreio = 0;
        var rastreios = await MedirAsync(() => RegistrarRastreioAsync(operador, pedidoId, "Correios", ++rastreio % 2 == 0 ? "BR-DESEMPENHO-2" : "BR-DESEMPENHO-1"));

        // Percentil 95 de 40 medições: a 38ª em ordem crescente.
        var (p95Consulta, p95Rastreio) = (consultas.Order().ElementAt(37), rastreios.Order().ElementAt(37));
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"p95 da consulta: {p95Consulta.TotalMilliseconds:F0} ms; p95 do rastreio: {p95Rastreio.TotalMilliseconds:F0} ms.");
        p95Consulta.Should().BeLessThan(Limite, "SC-004, consulta");
        p95Rastreio.Should().BeLessThan(Limite, "SC-004, rastreio");
    }

    private static async Task<List<TimeSpan>> MedirAsync(Func<Task<HttpResponseMessage>> operacao)
    {
        var duracoes = new List<TimeSpan>();
        for (var i = 0; i < Medicoes; i++)
        {
            var cronometro = Stopwatch.StartNew();
            var resposta = await operacao();
            duracoes.Add(cronometro.Elapsed);
            resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        return duracoes;
    }

    /// <summary>
    /// Pedido de 10 itens (CAT-001 a CAT-007 mais 3 produtos inseridos), levado a ProntoParaEnvio
    /// pela operação e com o histórico completado por SQL até 10 entradas.
    /// </summary>
    private static async Task<Guid> PedidoProntoAsync(string stringConexao, HttpClient cliente, HttpClient operador)
    {
        var extras = Enumerable.Range(1, 3).Select(_ => Guid.CreateVersion7()).ToList();
        foreach (var (id, indice) in extras.Select((id, indice) => (id, indice)))
        {
            await BancoDadosTeste.ExecutarAsync(stringConexao,
                "INSERT INTO Produtos (Id, Sku, Nome, Preco, QuantidadeEstoque, Ativo, CriadoEm, AtualizadoEm) " +
                $"VALUES ('{id}', 'RASTREIO-{indice}', N'Produto extra', 10, 1000, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())");
        }

        var itens = Enumerable.Range(1, 7).Select(Catalogo).Concat(extras).Select(id => (id, 1)).ToArray();
        var pedidoId = (await CriadoAsync(cliente, itens)).GetProperty("id").GetGuid();
        (await MudarStatusAsync(operador, pedidoId, "Processando")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await MudarStatusAsync(operador, pedidoId, "ProntoParaEnvio")).StatusCode.Should().Be(HttpStatusCode.OK);
        for (var i = 0; i < 7; i++)
        {
            await BancoDadosTeste.ExecutarAsync(stringConexao,
                "INSERT INTO HistoricosStatusPedido (PedidoId, StatusAnterior, StatusNovo, Motivo, OcorridoEm) " +
                $"VALUES ('{pedidoId}', 'Processando', 'Processando', N'Entrada de carga {i}', DATEADD(SECOND, -{100 - i}, SYSDATETIMEOFFSET()))");
        }

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM HistoricosStatusPedido WHERE PedidoId = '{pedidoId}'")).Should().Be(10);
        return pedidoId;
    }
}
