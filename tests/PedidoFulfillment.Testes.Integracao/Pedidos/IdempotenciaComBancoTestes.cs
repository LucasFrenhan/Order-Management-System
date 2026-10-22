using System.Net;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>User Story 2 com SQL Server real (cenários 1 a 6; <c>contracts/idempotencia.md</c>).</summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class IdempotenciaComBancoTestes(BancoDadosTeste banco)
{
    private const string ContarPedidos = "SELECT COUNT(*) FROM Pedidos";

    [Fact]
    public async Task Repeticao_devolve_a_mesma_resposta_sem_criar_pedido()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var chave = Guid.NewGuid();
        var original = await CriarAsync(cliente, chave, (Cat001, 2), (Cat002, 1));
        var corpoOriginal = await CorpoAsync(original);

        var repetida = await CriarAsync(cliente, chave, (Cat001, 2), (Cat002, 1));
        var outraOrdem = await CriarAsync(cliente, chave, (Cat002, 1), (Cat001, 2));

        foreach (var resposta in new[] { repetida, outraOrdem })
        {
            resposta.StatusCode.Should().Be(HttpStatusCode.Created);
            resposta.Headers.Location.Should().Be(original.Headers.Location);
            DeveSerIgualPorValor(await CorpoAsync(resposta), corpoOriginal);
        }

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarPedidos)).Should().Be(1);
    }

    [Fact]
    public async Task Mesma_chave_com_outro_conteudo_responde_409_divergente()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var chave = Guid.NewGuid();
        (await CriarAsync(cliente, chave, (Cat001, 1))).StatusCode.Should().Be(HttpStatusCode.Created);

        var resposta = await CriarAsync(cliente, chave, (Cat001, 2));

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(resposta)).Should().Be("IDEMPOTENCIA_CONTEUDO_DIVERGENTE");
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarPedidos)).Should().Be(1);
    }

    [Fact]
    public async Task A_chave_vale_por_cliente()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (clienteA, _, _) = await ClienteAsync(api);
        var (clienteB, _, _) = await ClienteAsync(api);
        var chave = Guid.NewGuid();

        var deA = await CriarAsync(clienteA, chave, (Cat001, 1));
        var deB = await CriarAsync(clienteB, chave, (Cat001, 1));

        deA.StatusCode.Should().Be(HttpStatusCode.Created);
        deB.StatusCode.Should().Be(HttpStatusCode.Created);
        deB.Headers.Location.Should().NotBe(deA.Headers.Location);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarPedidos)).Should().Be(2);
    }

    [Fact]
    public async Task Depois_de_um_422_a_mesma_chave_cria_o_pedido_corrigido()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var chave = Guid.NewGuid();
        (await CriarAsync(cliente, chave, (Cat009, 1))).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var corrigida = await CriarAsync(cliente, chave, (Cat001, 1));

        corrigida.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Repeticao_depois_do_cancelamento_continua_com_a_resposta_original()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var chave = Guid.NewGuid();
        var original = await CorpoAsync(await CriarAsync(cliente, chave, (Cat001, 1)));
        var id = original.GetProperty("id").GetGuid();
        (await MudarStatusAsync(cliente, id, "Cancelado", "Desisti")).StatusCode.Should().Be(HttpStatusCode.OK);

        var repetida = await CriarAsync(cliente, chave, (Cat001, 1));

        repetida.StatusCode.Should().Be(HttpStatusCode.Created);
        (await CorpoAsync(repetida)).GetProperty("status").GetString().Should().Be("Pendente");
    }

    /// <summary>Mesmos campos e valores, com números comparados pelo valor (achado M3 do analyze).</summary>
    private static void DeveSerIgualPorValor(JsonElement atual, JsonElement esperado)
    {
        atual.ValueKind.Should().Be(esperado.ValueKind);
        switch (esperado.ValueKind)
        {
            case JsonValueKind.Object:
                atual.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(esperado.EnumerateObject().Select(p => p.Name));
                foreach (var propriedade in esperado.EnumerateObject())
                {
                    DeveSerIgualPorValor(atual.GetProperty(propriedade.Name), propriedade.Value);
                }

                break;
            case JsonValueKind.Array:
                atual.GetArrayLength().Should().Be(esperado.GetArrayLength());
                foreach (var (item, indice) in esperado.EnumerateArray().Select((item, indice) => (item, indice)))
                {
                    DeveSerIgualPorValor(atual[indice], item);
                }

                break;
            case JsonValueKind.Number:
                atual.GetDecimal().Should().Be(esperado.GetDecimal());
                break;
            default:
                atual.ToString().Should().Be(esperado.ToString());
                break;
        }
    }
}
