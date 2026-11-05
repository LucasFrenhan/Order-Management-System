using System.Net;
using System.Text;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Autenticacao.CenarioAutenticacao;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>User Story 2 com SQL Server real: encerrar a sessão (REQ-24).</summary>
public sealed class LogoutTestes(BancoDadosTeste banco)
{
    [Fact]
    public async Task Logout_responde_204_e_o_token_nao_renova_mais()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        await using var api = Api(banco.NovoBanco(), logs: logs);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var tokens = await LoginAsync(cliente);

        var logout = await LogoutAsync(cliente, tokens.Renovacao);

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await logout.Content.ReadAsStringAsync(Cancelamento)).Should().BeEmpty();
        (await RenovarAsync(cliente, tokens.Renovacao)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3011);
    }

    [Fact]
    public async Task Repetir_ou_usar_token_inexistente_e_o_mesmo_204()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        await using var api = Api(banco.NovoBanco(), logs: logs);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var tokens = await LoginAsync(cliente);
        await LogoutAsync(cliente, tokens.Renovacao);

        (await LogoutAsync(cliente, tokens.Renovacao)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await LogoutAsync(cliente, "token-que-nao-existe")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3011, "só o encerramento real gera log");
    }

    [Fact]
    public async Task Logout_de_token_expirado_e_o_mesmo_204_sem_encerrar_outra_coisa()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var relogio = new RelogioAjustavel();
        await using var api = Api(banco.NovoBanco(), relogio: relogio);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var tokens = await LoginAsync(cliente);
        relogio.Agora += TimeSpan.FromDays(15);

        (await LogoutAsync(cliente, tokens.Renovacao)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Encerrar_uma_sessao_nao_afeta_a_outra_da_mesma_conta()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var primeira = await LoginAsync(cliente);
        var segunda = await LoginAsync(cliente);

        await LogoutAsync(cliente, primeira.Renovacao);

        (await RenovarAsync(cliente, primeira.Renovacao)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RenovarAsync(cliente, segunda.Renovacao)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task O_token_de_acesso_continua_valendo_depois_do_logout()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var tokens = await LoginAsync(cliente);

        await LogoutAsync(cliente, tokens.Renovacao);

        var pedidos = await api.CreateClient().ComToken(tokens.Acesso).GetAsync("/api/v1/pedidos", Cancelamento);
        pedidos.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sem_o_token_a_resposta_e_400()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());

        var resposta = await api.CreateClient().PostAsync(RotaLogout, new StringContent("{}", Encoding.UTF8, "application/json"), Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Acima_do_limite_a_resposta_e_429()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco(), new Dictionary<string, string?> { ["LimiteRequisicoes:Logout:Limite"] = "1" });
        var cliente = api.CreateClient();

        (await LogoutAsync(cliente, "a")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var barrada = await LogoutAsync(cliente, "a");

        barrada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        barrada.Headers.RetryAfter.Should().NotBeNull();
    }
}
