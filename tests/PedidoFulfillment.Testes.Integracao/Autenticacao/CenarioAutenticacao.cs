using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Emails;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Emails;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>O que os testes das rotas do ciclo de autenticação compartilham (REQ-24).</summary>
internal static class CenarioAutenticacao
{
    public const string Email = "maria@exemplo.com.br";
    public const string Senha = "SenhaForte1";
    public const string NovaSenha = "OutraSenha2";

    public const string RotaRegistro = "/api/v1/auth/register";
    public const string RotaLogin = "/api/v1/auth/login";
    public const string RotaRenovacao = "/api/v1/auth/renovacao";
    public const string RotaLogout = "/api/v1/auth/logout";
    public const string RotaTroca = "/api/v1/auth/troca-senha";
    public const string RotaRecuperacao = "/api/v1/auth/recuperacao-senha";
    public const string RotaRedefinicao = "/api/v1/auth/redefinicao-senha";
    public const string RotaConfirmacao = "/api/v1/auth/confirmacao-email";
    public const string RotaReenvio = "/api/v1/auth/confirmacao-email/reenvio";

    public static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    public static FabricaApi Api(
        string stringConexao, IReadOnlyDictionary<string, string?>? configuracoes = null, CapturaLogs? logs = null, TimeProvider? relogio = null, ColetorTelemetria? telemetria = null) => new()
        {
            PrepararBanco = true,
            StringConexao = stringConexao,
            Configuracoes = configuracoes ?? new Dictionary<string, string?>(),
            Logs = logs,
            Relogio = relogio,
            Telemetria = telemetria
        };

    public static async Task RegistrarAsync(HttpClient cliente, string email = Email, string senha = Senha)
    {
        var resposta = await cliente.PostAsJsonAsync(RotaRegistro, new { nome = "Maria Silva", email, senha }, Cancelamento);
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    public static Task<HttpResponseMessage> TentarLoginAsync(HttpClient cliente, string email = Email, string senha = Senha) =>
        cliente.PostAsJsonAsync(RotaLogin, new { email, senha }, Cancelamento);

    public static async Task<Tokens> LoginAsync(HttpClient cliente, string email = Email, string senha = Senha)
    {
        var resposta = await TentarLoginAsync(cliente, email, senha);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return await TokensAsync(resposta);
    }

    public static Task<HttpResponseMessage> RenovarAsync(HttpClient cliente, string token) =>
        cliente.PostAsJsonAsync(RotaRenovacao, new { tokenRenovacao = token }, Cancelamento);

    public static Task<HttpResponseMessage> LogoutAsync(HttpClient cliente, string token) =>
        cliente.PostAsJsonAsync(RotaLogout, new { tokenRenovacao = token }, Cancelamento);

    public static async Task<Tokens> TokensAsync(HttpResponseMessage resposta)
    {
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento);
        return new Tokens(
            corpo.GetProperty("tokenAcesso").GetString()!,
            corpo.GetProperty("tokenRenovacao").GetString()!,
            corpo.GetProperty("expiraEm").GetDateTimeOffset(),
            corpo.GetProperty("tokenRenovacaoExpiraEm").GetDateTimeOffset(),
            [.. corpo.GetProperty("perfis").EnumerateArray().Select(p => p.GetString()!)]);
    }

    /// <summary>A API com um enviador de e-mail para o processador usar, sem subir o Worker (R-11).</summary>
    public static FabricaApi ApiComEmail(
        string stringConexao, IEnviadorEmail enviador, IReadOnlyDictionary<string, string?>? configuracoes = null, CapturaLogs? logs = null, TimeProvider? relogio = null, ColetorTelemetria? telemetria = null) => new()
        {
            PrepararBanco = true,
            StringConexao = stringConexao,
            Configuracoes = configuracoes ?? new Dictionary<string, string?>(),
            Logs = logs,
            Relogio = relogio,
            Telemetria = telemetria,
            Servicos = servicos => servicos.AddSingleton(enviador)
        };

    /// <summary>Uma rodada do processador de e-mail sobre o banco da API.</summary>
    public static Task RodadaEmailsAsync(FabricaApi api, string stringConexao, OpcoesEmail? opcoes = null) =>
        new ProcessadorEmails(
                api.Services.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(opcoes ?? new OpcoesEmail()),
                Options.Create(new OpcoesSqlServer { StringConexao = stringConexao }),
                api.Services.GetRequiredService<TimeProvider>(),
                api.Services.GetRequiredService<ILogger<ProcessadorEmails>>())
            .ExecutarRodadaAsync(Cancelamento);

    public static Task<HttpResponseMessage> PedirRecuperacaoAsync(HttpClient cliente, string email = Email) =>
        cliente.PostAsJsonAsync(RotaRecuperacao, new { email }, Cancelamento);

    public static Task<HttpResponseMessage> RedefinirAsync(HttpClient cliente, string token, string novaSenha = NovaSenha) =>
        cliente.PostAsJsonAsync(RotaRedefinicao, new { token, novaSenha }, Cancelamento);

    /// <summary>Status, título, detalhe e código: o que precisa ser igual para não revelar o motivo.</summary>
    public static async Task<string> AssinaturaAsync(HttpResponseMessage resposta)
    {
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento);
        string? Campo(string nome) => corpo.TryGetProperty(nome, out var valor) ? valor.ToString() : null;
        return $"{(int)resposta.StatusCode}|{Campo("title")}|{Campo("detail")}|{Campo("codigo")}";
    }
}

internal sealed record Tokens(string Acesso, string Renovacao, DateTimeOffset ExpiraEm, DateTimeOffset RenovacaoExpiraEm, string[] Perfis);
