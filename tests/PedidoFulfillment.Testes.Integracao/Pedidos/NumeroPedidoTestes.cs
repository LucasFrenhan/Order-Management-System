using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>SC-003: 50 criações simultâneas com chaves diferentes recebem números distintos (FR-005).</summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class NumeroPedidoTestes(BancoDadosTeste banco)
{
    private const int Clientes = 5;
    private const int PorCliente = 10;

    [Fact]
    public async Task Criacoes_simultaneas_recebem_numeros_distintos_e_validos()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var clientes = new List<HttpClient>();
        for (var i = 0; i < Clientes; i++)
        {
            clientes.Add((await ClienteAsync(api)).Cliente);
        }

        var respostas = await Task.WhenAll(clientes
            .SelectMany(cliente => Enumerable.Range(0, PorCliente).Select(_ => cliente))
            .Select(cliente => Task.Run(() => CriarAsync(cliente, Guid.NewGuid(), (Cat001, 1)), TestContext.Current.CancellationToken)));

        respostas.Should().AllSatisfy(r => r.StatusCode.Should().Be(HttpStatusCode.Created));
        var numeros = new List<string>();
        foreach (var resposta in respostas)
        {
            numeros.Add((await CorpoAsync(resposta)).GetProperty("numero").GetString()!);
        }

        numeros.Should().HaveCount(Clientes * PorCliente).And.OnlyHaveUniqueItems();
        numeros.Should().AllSatisfy(n => n.Should().MatchRegex(@"^PED-\d{8}-\d{6}$"));
    }
}
