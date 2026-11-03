using System.Net;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>Atalhos para os testes da listagem de pedidos (REQ-21).</summary>
internal static class CenarioListagem
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> ListarAsync(HttpClient cliente, string consulta = "") =>
        cliente.GetAsync(consulta.Length == 0 ? Rota : $"{Rota}?{consulta}", Cancelamento);

    /// <summary>Lista e devolve a página (200 obrigatório).</summary>
    public static async Task<JsonElement> PaginaAsync(HttpClient cliente, string consulta = "")
    {
        var resposta = await ListarAsync(cliente, consulta);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync(Cancelamento));
        return await CorpoAsync(resposta);
    }

    public static List<string> Numeros(JsonElement pagina) =>
        [.. pagina.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("numero").GetString()!)];

    public static string? Proximo(JsonElement pagina) => Texto(pagina, "proximo");

    public static string? Anterior(JsonElement pagina) => Texto(pagina, "anterior");

    public static string Cursor(string? cursor) => $"cursor={Uri.EscapeDataString(cursor!)}";

    /// <summary>Cria um pedido de um item e devolve id e número.</summary>
    public static async Task<(Guid Id, string Numero)> PedidoAsync(HttpClient cliente)
    {
        var criado = await CriadoAsync(cliente, (Cat001, 1));
        return (criado.GetProperty("id").GetGuid(), criado.GetProperty("numero").GetString()!);
    }

    /// <summary>Grava datas e status direto no banco, para controlar a ordem e os filtros.</summary>
    public static Task AjustarAsync(string stringConexao, Guid pedidoId, DateTimeOffset criadoEm, DateTimeOffset? atualizadoEm = null, string? status = null)
    {
        var statusSql = status is null ? "" : $", Status = '{status}'";
        return BancoDadosTeste.ExecutarAsync(stringConexao,
            $"UPDATE Pedidos SET CriadoEm = '{criadoEm:O}', AtualizadoEm = '{atualizadoEm ?? criadoEm:O}'{statusSql} WHERE Id = '{pedidoId}'");
    }

    public static string Data(DateTimeOffset data) => Uri.EscapeDataString(data.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

    private static string? Texto(JsonElement pagina, string nome) =>
        pagina.GetProperty(nome).ValueKind == JsonValueKind.Null ? null : pagina.GetProperty(nome).GetString();
}
