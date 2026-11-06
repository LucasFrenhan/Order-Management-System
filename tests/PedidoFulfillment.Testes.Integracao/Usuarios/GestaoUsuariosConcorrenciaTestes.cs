using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Autenticacao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Usuarios.CenarioGestaoUsuarios;

namespace PedidoFulfillment.Testes.Integracao.Usuarios;

/// <summary>
/// O último Admin sob concorrência, entre duas instâncias da API sobre o mesmo banco (REQ-25, FR-014,
/// SC-003). Sem carga inicial: os dois Admins são os únicos do sistema.
/// </summary>
public sealed class GestaoUsuariosConcorrenciaTestes(BancoDadosTeste banco)
{
    private const int Rodadas = 50;
    private const int LoginsSimultaneos = 6;
    private const int SessoesDuranteTroca = 40;

    private sealed record Cenario(FabricaApi InstanciaA, FabricaApi InstanciaB, Guid AdminA, Guid AdminB, HttpClient ClienteA, HttpClient ClienteB) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await InstanciaA.DisposeAsync();
            await InstanciaB.DisposeAsync();
        }
    }

    private async Task<Cenario> PrepararAsync()
    {
        var conexao = banco.NovoBanco();
        var instanciaA = new FabricaApi { PrepararBanco = true, StringConexao = conexao };
        var adminA = await ContaAsync(instanciaA, "a@exemplo.com.br", perfis: [PerfilUsuario.Admin, PerfilUsuario.OperadorLogistica]);
        var adminB = await ContaAsync(instanciaA, "b@exemplo.com.br", perfis: [PerfilUsuario.Admin, PerfilUsuario.OperadorLogistica]);
        var instanciaB = new FabricaApi { StringConexao = conexao };
        return new Cenario(
            instanciaA, instanciaB, adminA, adminB,
            await ClienteDeAsync(instanciaA, "a@exemplo.com.br"),
            await ClienteDeAsync(instanciaB, "b@exemplo.com.br"));
    }

    private static async Task RestaurarAdminsAsync(Cenario c)
    {
        await using var escopo = c.InstanciaA.Services.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
        var admin = await contexto.Roles.Where(r => r.Name == nameof(PerfilUsuario.Admin)).Select(r => r.Id).SingleAsync(Cancelamento);
        await contexto.Users.ExecuteUpdateAsync(s => s.SetProperty(u => u.DesativadoEm, (DateTimeOffset?)null), Cancelamento);
        foreach (var id in new[] { c.AdminA, c.AdminB })
        {
            if (!await contexto.UserRoles.AnyAsync(p => p.UserId == id && p.RoleId == admin, Cancelamento))
            {
                contexto.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid> { UserId = id, RoleId = admin });
            }
        }

        await contexto.SaveChangesAsync(Cancelamento);
    }

    private static async Task<int> AdminsAtivosAsync(Cenario c)
    {
        var contaA = await LerContaAsync(c.InstanciaA, c.AdminA);
        var contaB = await LerContaAsync(c.InstanciaA, c.AdminB);
        return (contaA.EhAdminAtivo ? 1 : 0) + (contaB.EhAdminAtivo ? 1 : 0);
    }

    [Fact]
    public async Task Dois_Admins_retirando_o_perfil_um_do_outro_sempre_sobra_um()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var c = await PrepararAsync();

        for (var rodada = 0; rodada < Rodadas; rodada++)
        {
            await RestaurarAdminsAsync(c);

            var respostas = await Task.WhenAll(
                c.ClienteA.DeleteAsync($"{Rota}/{c.AdminB}/perfis/Admin", Cancelamento),
                c.ClienteB.DeleteAsync($"{Rota}/{c.AdminA}/perfis/Admin", Cancelamento));

            respostas.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Forbidden], $"na rodada {rodada}");
            (await AdminsAtivosAsync(c)).Should().Be(1, $"na rodada {rodada}");
        }
    }

    [Fact]
    public async Task Dois_Admins_desativando_um_ao_outro_sempre_sobra_um()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var c = await PrepararAsync();

        for (var rodada = 0; rodada < Rodadas; rodada++)
        {
            await RestaurarAdminsAsync(c);

            var respostas = await Task.WhenAll(
                c.ClienteA.PostAsync($"{Rota}/{c.AdminB}/desativacao", null, Cancelamento),
                c.ClienteB.PostAsync($"{Rota}/{c.AdminA}/desativacao", null, Cancelamento));

            respostas.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Forbidden], $"na rodada {rodada}");
            (await AdminsAtivosAsync(c)).Should().Be(1, $"na rodada {rodada}");
        }
    }

    [Fact]
    public async Task Desativacao_e_retirada_de_perfil_cruzadas_sempre_deixam_um()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var c = await PrepararAsync();

        for (var rodada = 0; rodada < Rodadas; rodada++)
        {
            await RestaurarAdminsAsync(c);

            var respostas = await Task.WhenAll(
                c.ClienteA.PostAsync($"{Rota}/{c.AdminB}/desativacao", null, Cancelamento),
                c.ClienteB.DeleteAsync($"{Rota}/{c.AdminA}/perfis/Admin", Cancelamento));

            respostas.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Forbidden], $"na rodada {rodada}");
            (await AdminsAtivosAsync(c)).Should().Be(1, $"na rodada {rodada}");
        }
    }

    [Fact]
    public async Task Login_simultaneo_a_desativacao_nao_deixa_sessao_que_volte_a_valer()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var c = await PrepararAsync();
        var id = await ContaAsync(c.InstanciaA, "cliente@exemplo.com.br", perfis: PerfilUsuario.Cliente);
        var anonimos = Enumerable.Range(0, LoginsSimultaneos).Select(_ => c.InstanciaA.CreateClient()).ToList();

        for (var rodada = 0; rodada < Rodadas; rodada++)
        {
            // Vários logins, com a desativação em instantes diferentes do fim deles: o momento do deadlock é estreito.
            var logins = anonimos.Select(a => CenarioAutenticacao.TentarLoginAsync(a, "cliente@exemplo.com.br", SenhaPadrao)).ToList();
            await Task.Delay(Random.Shared.Next(0, 120), Cancelamento);
            var desativacao = await c.ClienteA.PostAsync($"{Rota}/{id}/desativacao", null, Cancelamento);
            var respostas = await Task.WhenAll(logins);
            desativacao.StatusCode.Should().Be(HttpStatusCode.OK, $"na rodada {rodada}");
            respostas.Select(r => r.StatusCode).Should().OnlyContain(s => s == HttpStatusCode.OK || s == HttpStatusCode.Unauthorized, $"na rodada {rodada}");
            (await c.ClienteA.PostAsync($"{Rota}/{id}/reativacao", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK, $"na rodada {rodada}");

            foreach (var resposta in respostas.Where(r => r.StatusCode == HttpStatusCode.OK))
            {
                var tokens = await CenarioAutenticacao.TokensAsync(resposta);
                (await CenarioAutenticacao.RenovarAsync(anonimos[0], tokens.Renovacao)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"na rodada {rodada}");
            }
        }
    }

    /// <summary>
    /// A sessão é criada direto, sem o login: um login que perde a corrida para a senha nova conta erro para o
    /// bloqueio, e isso não é o que este teste observa.
    /// </summary>
    [Fact]
    public async Task Sessao_criada_durante_a_troca_de_senha_nao_gera_deadlock()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var c = await PrepararAsync();
        var (atual, nova) = (SenhaPadrao, "OutraSenha2");
        var validades = new ConfiguracaoSessoes(TimeSpan.FromMinutes(30), TimeSpan.FromHours(8));

        for (var rodada = 0; rodada < Rodadas; rodada++)
        {
            // A transação só começa depois dos hashes da senha: as sessões se espalham para alcançá-la.
            var sessoes = Enumerable.Range(0, SessoesDuranteTroca).Select(_ => CriarSessaoAsync(c.InstanciaA, c.AdminA, validades, Random.Shared.Next(0, 500))).ToList();
            var troca = await c.ClienteA.PostAsJsonAsync(CenarioAutenticacao.RotaTroca, new { senhaAtual = atual, novaSenha = nova }, Cancelamento);
            await Task.WhenAll(sessoes);
            troca.StatusCode.Should().Be(HttpStatusCode.NoContent, $"na rodada {rodada}");
            (atual, nova) = (nova, atual);
        }
    }

    private static async Task CriarSessaoAsync(FabricaApi api, Guid usuarioId, ConfiguracaoSessoes validades, int esperaMilissegundos)
    {
        await Task.Delay(esperaMilissegundos, Cancelamento);
        await using var escopo = api.Services.CreateAsyncScope();
        await escopo.ServiceProvider.GetRequiredService<IArmazenamentoSessoes>()
            .CriarSessaoAsync(usuarioId, RandomNumberGenerator.GetBytes(32), DateTimeOffset.UtcNow, validades, Cancelamento);
    }
}
