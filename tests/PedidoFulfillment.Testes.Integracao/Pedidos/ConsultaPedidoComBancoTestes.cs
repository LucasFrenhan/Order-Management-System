using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>User Story 3 com SQL Server real (cenários 1 a 4).</summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class ConsultaPedidoComBancoTestes(BancoDadosTeste banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Dono_e_operacao_veem_o_pedido_e_os_demais_recebem_o_mesmo_404()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var (dono, _, _) = await ClienteAsync(api);
        var (outro, _, _) = await ClienteAsync(api);
        var criado = await CriadoAsync(dono, (Cat001, 1));
        var rota = $"{Rota}/{criado.GetProperty("id").GetGuid()}";

        foreach (var cliente in new[] { dono, await AdminAsync(api), await OperadorAsync(api) })
        {
            var resposta = await cliente.GetAsync(rota, Cancelamento);
            resposta.StatusCode.Should().Be(HttpStatusCode.OK);
            (await CorpoAsync(resposta)).GetProperty("numero").GetString().Should().Be(criado.GetProperty("numero").GetString());
        }

        var deOutro = await outro.GetAsync(rota, Cancelamento);
        var inexistente = await dono.GetAsync($"{Rota}/{Guid.NewGuid()}", Cancelamento);
        deOutro.StatusCode.Should().Be(HttpStatusCode.NotFound);
        inexistente.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var (corpoOutro, corpoInexistente) = (await CorpoAsync(deOutro), await CorpoAsync(inexistente));
        corpoOutro.GetProperty("title").GetString().Should().Be(corpoInexistente.GetProperty("title").GetString());
        corpoOutro.GetProperty("detail").GetString().Should().Be(corpoInexistente.GetProperty("detail").GetString());
        (await dono.GetAsync($"{Rota}/nao-e-guid", Cancelamento)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
