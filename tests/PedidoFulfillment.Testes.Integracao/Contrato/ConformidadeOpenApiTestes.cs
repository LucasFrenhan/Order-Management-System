using FluentAssertions;
using Microsoft.OpenApi;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Contrato;

/// <summary>
/// O documento gerado pelo código deve ser conforme ao contrato aprovado (FR-024, SC-005).
/// Cada história acrescenta as linhas das suas operações e schemas.
/// </summary>
public sealed class ConformidadeOpenApiTestes(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    public static TheoryData<string> SchemasComuns =>
    [
        "ProblemDetails",
        "ValidacaoProblemDetails",
        "NegocioProblemDetails",
        "PerfilUsuario",
        "StatusPedido"
    ];

    public static TheoryData<string, string> Operacoes => new()
    {
        { "POST", "/api/v1/auth/register" },
        { "POST", "/api/v1/auth/login" },
        { "POST", "/api/v1/pedidos" },
        { "GET", "/api/v1/pedidos/{id}" },
        { "PATCH", "/api/v1/pedidos/{id}/status" }
    };

    [Theory]
    [MemberData(nameof(SchemasComuns))]
    public async Task Schema_comum_conforme_ao_contrato(string schema)
    {
        var comparador = await CriarComparadorAsync();

        comparador.CompararSchemaComponente(schema).Should().BeEmpty();
    }

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Operacoes))]
    public async Task Operacao_conforme_ao_contrato(string metodo, string caminho)
    {
        var comparador = await CriarComparadorAsync();

        comparador.CompararOperacao(new HttpMethod(metodo), caminho).Should().BeEmpty();
    }

    [Fact]
    public async Task Documento_gerado_nao_documenta_respostas_501()
    {
        var gerado = await LeitorContrato.CarregarGeradoAsync(fabrica.CreateClient());

        var respostas = gerado.Paths.Values.SelectMany(p => (p.Operations ?? []).Values)
            .SelectMany(o => (o.Responses ?? []).Keys);
        respostas.Should().NotContain("501");
    }

    private async Task<ComparadorContrato> CriarComparadorAsync() =>
        new(await LeitorContrato.CarregarContratoAsync(), await LeitorContrato.CarregarGeradoAsync(fabrica.CreateClient()));
}
