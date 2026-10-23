using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Serializacao;

/// <summary>
/// DTOs de pedido serializados com as opções JSON reais da API (FR-019, FR-021, FR-022).
/// </summary>
public sealed class SerializacaoPedidoTestes(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    [Fact]
    public void Pedido_serializa_em_camel_case_com_status_texto_datas_utc_e_valores_com_2_casas()
    {
        var json = JsonSerializer.SerializeToElement(Exemplos.Pedido(), OpcoesJson.DaApi(fabrica));

        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "id", "numero", "clienteId", "status", "itens", "valorTotal", "moeda", "criadoEm", "atualizadoEm", "historico", "entrega");
        json.GetProperty("historico").ValueKind.Should().Be(JsonValueKind.Array, "REQ-11: sempre presente");
        json.GetProperty("entrega").ValueKind.Should().Be(JsonValueKind.Null, "REQ-11: sem ordem de entrega");
        json.GetProperty("status").GetString().Should().Be("Pendente");
        json.GetProperty("moeda").GetString().Should().Be("BRL");
        json.GetProperty("criadoEm").GetString().Should().Be("2026-10-21T15:30:00+00:00");
        json.GetProperty("valorTotal").GetRawText().Should().Be("829.70");
        var item = json.GetProperty("itens")[0];
        item.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            "produtoId", "nomeProduto", "quantidade", "precoUnitario", "subtotal");
        item.GetProperty("precoUnitario").GetRawText().Should().Be("349.90");
        item.GetProperty("subtotal").GetRawText().Should().Be("699.80");
    }

    [Fact]
    public void Criar_pedido_ignora_campos_desconhecidos()
    {
        const string json = """{"itens":[{"produtoId":"7c9e6679-7425-40de-944b-e07fc1f90ae7","quantidade":2,"extra":true}],"novoCampo":"x"}""";

        var pedido = JsonSerializer.Deserialize<CriarPedidoRequestDTO>(json, OpcoesJson.DaApi(fabrica));

        pedido!.Itens.Should().ContainSingle().Which.Quantidade.Should().Be(2);
    }
}
