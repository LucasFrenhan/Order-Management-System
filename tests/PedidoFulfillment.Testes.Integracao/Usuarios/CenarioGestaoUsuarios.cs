using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Emails;
using PedidoFulfillment.Application.Usuarios;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Infrastructure.Identidade;
using PedidoFulfillment.Testes.Integracao.Autenticacao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Usuarios;

/// <summary>O que os testes da gestão de usuários compartilham (REQ-25): um Admin real, da carga inicial.</summary>
internal static class CenarioGestaoUsuarios
{
    public const string Rota = "/api/v1/admin/usuarios";
    public const string EmailAdmin = "admin@exemplo.com.br";
    public const string SenhaPadrao = "SenhaForte1";

    public static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    public static readonly IReadOnlyDictionary<string, string?> AdminInicial = new Dictionary<string, string?>

    {
        ["UsuariosIniciais:0:Nome"] = "Admin Inicial",
        ["UsuariosIniciais:0:Email"] = EmailAdmin,
        ["UsuariosIniciais:0:Senha"] = SenhaPadrao,
        ["UsuariosIniciais:0:Perfil"] = nameof(PerfilUsuario.Admin),
    };

    public static FabricaApi Api(string conexao, CapturaLogs? logs = null, ColetorTelemetria? telemetria = null, IEnviadorEmail? enviador = null) => new()
    {
        PrepararBanco = true,
        StringConexao = conexao,
        Configuracoes = AdminInicial,
        Logs = logs,
        Telemetria = telemetria,
        Servicos = enviador is null ? null : s => s.AddSingleton(enviador)
    };

    /// <summary>Um cliente HTTP autenticado com o token real de um login.</summary>
    public static async Task<HttpClient> ClienteDeAsync(FabricaApi api, string email = EmailAdmin, string senha = SenhaPadrao)
    {
        var tokens = await CenarioAutenticacao.LoginAsync(api.CreateClient(), email, senha);
        return api.CreateClient().ComToken(tokens.Acesso);
    }

    /// <summary>Uma conta com senha e os perfis pedidos, direto pelo Identity. Devolve o id.</summary>
    public static async Task<Guid> ContaAsync(FabricaApi api, string email, string nome = "Conta Teste", params PerfilUsuario[] perfis)
    {
        await using var escopo = api.Services.CreateAsyncScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var usuario = new Usuario { UserName = email, Email = email, Nome = nome, EmailConfirmed = true };
        (await usuarios.CreateAsync(usuario, SenhaPadrao)).Succeeded.Should().BeTrue();
        foreach (var perfil in perfis)
        {
            (await usuarios.AddToRoleAsync(usuario, perfil.ToString())).Succeeded.Should().BeTrue();
        }

        return usuario.Id;
    }

    public static async Task<Guid> IdDeAsync(FabricaApi api, string email)
    {
        await using var escopo = api.Services.CreateAsyncScope();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        return (await usuarios.FindByEmailAsync(email))!.Id;
    }

    public static async Task<ContaGestao> LerContaAsync(FabricaApi api, Guid id)
    {
        await using var escopo = api.Services.CreateAsyncScope();
        return (await escopo.ServiceProvider.GetRequiredService<IGestorUsuarios>().ObterAsync(id, Cancelamento))!;
    }

    public static async Task<JsonElement> CorpoAsync(HttpResponseMessage resposta) =>
        await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento);

    public static async Task<string?> CodigoAsync(HttpResponseMessage resposta) =>
        (await CorpoAsync(resposta)).TryGetProperty("codigo", out var codigo) ? codigo.GetString() : null;

    public static async Task DeveRecusarAsync(HttpResponseMessage resposta, HttpStatusCode status, string? codigo = null)
    {
        resposta.StatusCode.Should().Be(status);
        if (codigo is not null)
        {
            (await CodigoAsync(resposta)).Should().Be(codigo);
        }
    }
}
