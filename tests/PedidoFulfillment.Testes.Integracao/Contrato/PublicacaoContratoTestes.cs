using System.Net;
using FluentAssertions;
using Microsoft.OpenApi;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Contrato;

/// <summary>
/// User Story 1: contrato publicado e navegável fora de produção (R-03, R-04, FR-007a).
/// </summary>
public sealed class PublicacaoContratoTestes(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    public static TheoryData<string> EnderecosDocumentacao => ["/openapi/v1.yaml", "/openapi/v1.json", "/scalar/v1"];

    [Theory]
    [MemberData(nameof(EnderecosDocumentacao))]
    public async Task Documentacao_fica_publica_fora_de_producao(string endereco)
    {
        var resposta = await fabrica.CreateClient().GetAsync(endereco, TestContext.Current.CancellationToken);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Yaml_publicado_e_identico_ao_contrato_aprovado()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/openapi/v1.yaml", TestContext.Current.CancellationToken);

        resposta.Content.Headers.ContentType?.MediaType.Should().Be("application/yaml");
        var publicado = await resposta.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        publicado.Should().Equal(await File.ReadAllBytesAsync(LeitorContrato.CaminhoContrato, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Documento_gerado_declara_openapi_31_versao_v1_e_bearer()
    {
        var documento = await LeitorContrato.CarregarGeradoAsync(fabrica.CreateClient());
        var json = await fabrica.CreateClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        json.Should().Contain("\"openapi\": \"3.1");
        documento.Info.Version.Should().Be("v1");
        var esquema = documento.Components!.SecuritySchemes!["bearerAuth"];
        esquema.Type.Should().Be(SecuritySchemeType.Http);
        esquema.Scheme.Should().Be("bearer");
        esquema.BearerFormat.Should().Be("JWT");
    }

    [Fact]
    public async Task Interface_de_navegacao_aponta_para_o_contrato_aprovado()
    {
        var html = await fabrica.CreateClient().GetStringAsync("/scalar/v1", TestContext.Current.CancellationToken);

        html.Should().Contain("openapi/v1.yaml");
    }

    [Theory]
    [MemberData(nameof(EnderecosDocumentacao))]
    public async Task Em_producao_documentacao_nao_existe(string endereco)
    {
        await using var producao = new FabricaApi { Ambiente = "Production" };

        var semToken = await producao.CreateClient().GetAsync(endereco, TestContext.Current.CancellationToken);
        var comToken = await producao.CreateClient().ComToken(PerfilUsuario.Admin)
            .GetAsync(endereco, TestContext.Current.CancellationToken);

        semToken.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        comToken.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
