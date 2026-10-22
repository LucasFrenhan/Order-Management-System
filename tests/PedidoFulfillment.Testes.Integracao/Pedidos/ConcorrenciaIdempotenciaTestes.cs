using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>
/// SC-002: 20 criações simultâneas com a mesma chave e o mesmo conteúdo criam exatamente um
/// pedido, sem nenhum 500, em 10 repetições (FR-012).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class ConcorrenciaIdempotenciaTestes(BancoDadosTeste banco)
{
    private const int Repeticoes = 10;
    private const int Simultaneas = 20;

    [Fact]
    public async Task Requisicoes_simultaneas_com_a_mesma_chave_criam_um_unico_pedido()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);

        for (var repeticao = 0; repeticao < Repeticoes; repeticao++)
        {
            var chave = Guid.NewGuid();

            var respostas = await Task.WhenAll(Enumerable.Range(0, Simultaneas)
                .Select(_ => Task.Run(() => CriarAsync(cliente, chave, (Cat001, 1)), TestContext.Current.CancellationToken)));

            respostas.Should().NotContain(r => r.StatusCode == HttpStatusCode.InternalServerError, $"repetição {repeticao}");
            var criadas = respostas.Where(r => r.StatusCode == HttpStatusCode.Created).ToList();
            criadas.Should().NotBeEmpty();
            criadas.Select(r => r.Headers.Location).Distinct().Should().ContainSingle($"repetição {repeticao}");
            foreach (var recusada in respostas.Where(r => r.StatusCode != HttpStatusCode.Created))
            {
                recusada.StatusCode.Should().Be(HttpStatusCode.Conflict);
                (await CodigoAsync(recusada)).Should().Be("IDEMPOTENCIA_EM_PROCESSAMENTO");
            }

            (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
                $"SELECT COUNT(*) FROM Pedidos p JOIN ChavesIdempotencia c ON c.PedidoId = p.Id WHERE c.Chave = '{chave}'"))
                .Should().Be(1, $"repetição {repeticao}");
        }

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM Pedidos")).Should().Be(Repeticoes);
    }
}
