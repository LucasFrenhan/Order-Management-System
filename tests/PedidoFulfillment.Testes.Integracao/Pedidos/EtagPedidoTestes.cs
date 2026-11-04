using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary><c>ETag</c> e <c>304</c> na consulta do pedido (REQ-18, US5 cenários 1 a 3; FR-025, research R-17).</summary>
public sealed class EtagPedidoTestes(BancoDadosTeste banco)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> ConsultarAsync(HttpClient usuario, Guid id, string? seNaoCorresponder = null)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Get, $"{Rota}/{id}");
        if (seNaoCorresponder is not null)
        {
            requisicao.Headers.TryAddWithoutValidation("If-None-Match", seNaoCorresponder);
        }

        return usuario.SendAsync(requisicao, Token);
    }

    [Fact]
    public async Task Mesma_versao_responde_304_sem_corpo_e_cada_mudanca_troca_o_ETag()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var id = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        var primeira = await ConsultarAsync(cliente, id);
        var etag = primeira.Headers.ETag!;
        primeira.StatusCode.Should().Be(HttpStatusCode.OK);
        etag.IsWeak.Should().BeFalse();

        var repetida = await ConsultarAsync(cliente, id, etag.Tag);
        repetida.StatusCode.Should().Be(HttpStatusCode.NotModified);
        (await repetida.Content.ReadAsByteArrayAsync(Token)).Should().BeEmpty();
        repetida.Headers.ETag!.Tag.Should().Be(etag.Tag);

        var etags = new List<string> { etag.Tag };
        (await MudarStatusAsync(operador, id, "Processando")).EnsureSuccessStatusCode();
        etags.Add(await NovaVersaoAsync(cliente, id, etags[^1]));
        (await MudarStatusAsync(operador, id, "ProntoParaEnvio")).EnsureSuccessStatusCode();
        (await RegistrarRastreioAsync(operador, id, "Correios", "CC123456789BR")).EnsureSuccessStatusCode();
        etags.Add(await NovaVersaoAsync(cliente, id, etags[^1]));
        (await RegistrarRastreioAsync(operador, id, "Correios", "CC987654321BR")).EnsureSuccessStatusCode();
        etags.Add(await NovaVersaoAsync(cliente, id, etags[^1]));

        etags.Should().OnlyHaveUniqueItems("status, despacho e correção do rastreio trocam a versão");
    }

    private static async Task<string> NovaVersaoAsync(HttpClient usuario, Guid id, string antiga)
    {
        var resposta = await ConsultarAsync(usuario, id, antiga);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CorpoAsync(resposta)).GetProperty("id").GetGuid().Should().Be(id);
        return resposta.Headers.ETag!.Tag;
    }

    [Theory]
    [InlineData("nao-e-etag")]
    [InlineData("\"outra\"")]
    [InlineData("*")]
    public async Task If_None_Match_diferente_ou_malformado_responde_200(string valor)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var (cliente, _, _) = await ClienteAsync(api);
        var id = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        (await ConsultarAsync(cliente, id, valor)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pedido_de_outro_Cliente_continua_404_com_ou_sem_If_None_Match()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var (dono, _, _) = await ClienteAsync(api);
        var (outro, _, _) = await ClienteAsync(api);
        var id = (await CriadoAsync(dono, (Cat001, 1))).GetProperty("id").GetGuid();
        var etag = (await ConsultarAsync(dono, id)).Headers.ETag!.Tag;

        var semCabecalho = await ConsultarAsync(outro, id);
        var comCabecalho = await ConsultarAsync(outro, id, etag);

        semCabecalho.StatusCode.Should().Be(HttpStatusCode.NotFound);
        comCabecalho.StatusCode.Should().Be(HttpStatusCode.NotFound);
        comCabecalho.Headers.ETag.Should().BeNull();
    }
}
