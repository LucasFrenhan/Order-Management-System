using System.Diagnostics;
using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>SC-005: 95% das criações com 10 itens em menos de 500 ms no ambiente local.</summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class DesempenhoCriacaoTestes(BancoDadosTeste banco)
{
    private const int Aquecimento = 5;
    private const int Medicoes = 40;
    private static readonly TimeSpan Limite = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task Noventa_e_cinco_por_cento_das_criacoes_levam_menos_de_500_ms()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        // Do catálogo, CAT-001 a CAT-007 têm estoque; mais 3 produtos completam os 10 itens.
        var extras = Enumerable.Range(1, 3).Select(_ => Guid.CreateVersion7()).ToList();
        foreach (var (id, indice) in extras.Select((id, indice) => (id, indice)))
        {
            await BancoDadosTeste.ExecutarAsync(stringConexao,
                "INSERT INTO Produtos (Id, Sku, Nome, Preco, QuantidadeEstoque, Ativo, CriadoEm, AtualizadoEm) " +
                $"VALUES ('{id}', 'EXTRA-{indice}', N'Produto extra', 10, 1000, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())");
        }

        var itens = Enumerable.Range(1, 7).Select(Catalogo).Concat(extras).Select(id => (id, 1)).ToArray();
        for (var i = 0; i < Aquecimento; i++)
        {
            (await CriarAsync(cliente, Guid.NewGuid(), itens)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var duracoes = new List<TimeSpan>();
        for (var i = 0; i < Medicoes; i++)
        {
            var cronometro = Stopwatch.StartNew();
            var resposta = await CriarAsync(cliente, Guid.NewGuid(), itens);
            duracoes.Add(cronometro.Elapsed);
            resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Percentil 95 de 40 medições: a 38ª em ordem crescente.
        duracoes.Order().ElementAt(37).Should().BeLessThan(Limite);
    }
}
