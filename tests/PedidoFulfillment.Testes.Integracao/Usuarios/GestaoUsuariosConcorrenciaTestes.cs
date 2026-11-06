using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
        var anonimo = c.InstanciaA.CreateClient();

        for (var rodada = 0; rodada < 20; rodada++)
        {
            var login = CenarioAutenticacao.TentarLoginAsync(anonimo, "cliente@exemplo.com.br", SenhaPadrao);
            var desativacao = c.ClienteA.PostAsync($"{Rota}/{id}/desativacao", null, Cancelamento);
            await Task.WhenAll(login, desativacao);
            (await desativacao).StatusCode.Should().Be(HttpStatusCode.OK);
            var resposta = await login;
            (await c.ClienteA.PostAsync($"{Rota}/{id}/reativacao", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);

            if (resposta.StatusCode == HttpStatusCode.OK)
            {
                var tokens = await CenarioAutenticacao.TokensAsync(resposta);
                (await CenarioAutenticacao.RenovarAsync(anonimo, tokens.Renovacao)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"na rodada {rodada}");
            }
        }
    }
}
