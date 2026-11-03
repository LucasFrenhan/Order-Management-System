using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Produtos;

/// <summary>Atalhos dos testes do catálogo de produtos (REQ-22).</summary>
internal static class CenarioProdutos
{
    public const string RotaProdutos = "/api/v1/produtos";

    /// <summary>Prazo da busca para refletir uma gravação (FR-007a).</summary>
    public static readonly TimeSpan PrazoBusca = TimeSpan.FromSeconds(10);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    public static Task<HttpResponseMessage> CadastrarAsync(HttpClient admin, string sku, string nome, decimal preco = 10m, int estoque = 10, string? descricao = null) =>
        admin.PostAsJsonAsync(RotaProdutos, new { sku, nome, descricao, preco, estoqueInicial = estoque }, Cancelamento);

    /// <summary>Cadastra (201 obrigatório) e devolve o corpo.</summary>
    public static async Task<JsonElement> CadastradoAsync(HttpClient admin, string sku, string nome, decimal preco = 10m, int estoque = 10, string? descricao = null)
    {
        var resposta = await CadastrarAsync(admin, sku, nome, preco, estoque, descricao);
        resposta.StatusCode.Should().Be(HttpStatusCode.Created, await resposta.Content.ReadAsStringAsync(Cancelamento));
        return await CorpoAsync(resposta);
    }

    public static Task<HttpResponseMessage> ListarProdutosAsync(HttpClient cliente, string consulta = "") =>
        cliente.GetAsync(consulta.Length == 0 ? RotaProdutos : $"{RotaProdutos}?{consulta}", Cancelamento);

    public static async Task<JsonElement> PaginaProdutosAsync(HttpClient cliente, string consulta = "")
    {
        var resposta = await ListarProdutosAsync(cliente, consulta);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync(Cancelamento));
        return await CorpoAsync(resposta);
    }

    public static List<string> Skus(JsonElement pagina) =>
        [.. pagina.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("sku").GetString()!)];

    public static string? ProximoProdutos(JsonElement pagina) =>
        pagina.GetProperty("proximo").ValueKind == JsonValueKind.Null ? null : pagina.GetProperty("proximo").GetString();

    public static string? AnteriorProdutos(JsonElement pagina) =>
        pagina.GetProperty("anterior").ValueKind == JsonValueKind.Null ? null : pagina.GetProperty("anterior").GetString();

    public static string Busca(string texto) => $"busca={Uri.EscapeDataString(texto)}";

    /// <summary>
    /// Repete a busca até o resultado ser o esperado, dentro do prazo (o índice full-text é
    /// atualizado em segundo plano). Devolve o último resultado, para a asserção.
    /// </summary>
    public static async Task<List<string>> BuscarAteAsync(HttpClient cliente, string texto, Func<List<string>, bool> pronto, string consultaExtra = "")
    {
        var cronometro = Stopwatch.StartNew();
        while (true)
        {
            var skus = Skus(await PaginaProdutosAsync(cliente, $"{Busca(texto)}{consultaExtra}"));
            if (pronto(skus) || cronometro.Elapsed > PrazoBusca)
            {
                return skus;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), Cancelamento);
        }
    }

    public static Task<HttpResponseMessage> AjustarAsync(HttpClient admin, Guid produtoId, int variacao, string motivo = "Teste") =>
        admin.PostAsJsonAsync($"{RotaProdutos}/{produtoId}/ajustes-estoque", new { variacao, motivo }, Cancelamento);

    public static Task<HttpResponseMessage> DesativarAsync(HttpClient admin, Guid produtoId) =>
        admin.PostAsync($"{RotaProdutos}/{produtoId}/desativacao", null, Cancelamento);

    public static Task<HttpResponseMessage> AtivarAsync(HttpClient admin, Guid produtoId) =>
        admin.PostAsync($"{RotaProdutos}/{produtoId}/ativacao", null, Cancelamento);

    public static string NovoSku(string prefixo = "TST") => $"{prefixo}-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
}
