using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Autenticacao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>
/// API com banco real, catálogo inicial e usuários Admin e OperadorLogistica de teste, mais
/// atalhos para tokens e criação de pedidos.
/// </summary>
internal static class CenarioPedidos
{
    public const string Rota = "/api/v1/pedidos";

    public static readonly Guid Cat001 = Catalogo(1);
    public static readonly Guid Cat002 = Catalogo(2);
    public static readonly Guid Cat008 = Catalogo(8);
    public static readonly Guid Cat009 = Catalogo(9);
    public static readonly Guid Cat010 = Catalogo(10);

    private const string Senha = "SenhaForte1";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    public static Guid Catalogo(int numero) => Guid.Parse($"0192b6a0-0000-7000-8000-{numero:D12}");

    public static FabricaApi Api(string stringConexao, CapturaLogs? logs = null) => new()
    {
        PrepararBanco = true,
        StringConexao = stringConexao,
        Configuracoes = UsuariosIniciaisComBancoTestes.Configuracao(),
        Logs = logs
    };

    /// <summary>Registra um Cliente novo e devolve o cliente HTTP com o token dele, e o e-mail.</summary>
    public static async Task<(HttpClient Cliente, string Email, string Token)> ClienteAsync(FabricaApi api)
    {
        var anonimo = api.CreateClient();
        var email = $"cliente-{Guid.NewGuid():N}@exemplo.com.br";
        var registro = await anonimo.PostAsJsonAsync("/api/v1/auth/register", new { nome = "Cliente Teste", email, senha = Senha }, Cancelamento);
        registro.StatusCode.Should().Be(HttpStatusCode.Created);
        var token = await LoginComBancoTestes.TokenAsync(anonimo, email, Senha);
        return (api.CreateClient().ComToken(token), email, token);
    }

    public static async Task<HttpClient> AdminAsync(FabricaApi api) =>
        api.CreateClient().ComToken(await LoginComBancoTestes.TokenAsync(api.CreateClient(), UsuariosIniciaisComBancoTestes.EmailAdmin, UsuariosIniciaisComBancoTestes.SenhaAdmin));

    public static async Task<HttpClient> OperadorAsync(FabricaApi api) =>
        api.CreateClient().ComToken(await LoginComBancoTestes.TokenAsync(api.CreateClient(), UsuariosIniciaisComBancoTestes.EmailOperador, UsuariosIniciaisComBancoTestes.SenhaOperador));

    public static Task<HttpResponseMessage> CriarAsync(HttpClient cliente, Guid chave, params (Guid Produto, int Quantidade)[] itens)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, Rota)
        {
            Content = JsonContent.Create(new { itens = itens.Select(i => new { produtoId = i.Produto, quantidade = i.Quantidade }) })
        };
        requisicao.Headers.Add("X-Idempotency-Key", chave.ToString());
        return cliente.SendAsync(requisicao, Cancelamento);
    }

    /// <summary>Cria e devolve o corpo do pedido criado (201 obrigatório).</summary>
    public static async Task<JsonElement> CriadoAsync(HttpClient cliente, params (Guid Produto, int Quantidade)[] itens)
    {
        var resposta = await CriarAsync(cliente, Guid.NewGuid(), itens);
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        return await CorpoAsync(resposta);
    }

    public static Task<HttpResponseMessage> MudarStatusAsync(HttpClient cliente, Guid pedidoId, string status, string? motivo = null) =>
        cliente.PatchAsJsonAsync($"{Rota}/{pedidoId}/status", new { status, motivo }, Cancelamento);

    public static Task<JsonElement> CorpoAsync(HttpResponseMessage resposta) =>
        resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento);

    public static async Task<string?> CodigoAsync(HttpResponseMessage resposta) =>
        (await CorpoAsync(resposta)).TryGetProperty("codigo", out var codigo) ? codigo.GetString() : null;
}
