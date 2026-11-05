using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Infrastructure.Identidade;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Autenticacao.CenarioAutenticacao;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>User Story 1 com SQL Server real: login com token de renovação, rotação e reutilização (REQ-24).</summary>
public sealed class SessoesTestes(BancoDadosTeste banco)
{
    private const string Invalida = "401|Sessão inválida.|Faça login novamente.|";

    private const string RotaPedidos = "/api/v1/pedidos";

    [Fact]
    public async Task Login_traz_o_token_de_renovacao_e_o_acesso_vale_15_minutos()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);

        var tokens = await LoginAsync(cliente);

        tokens.Renovacao.Should().HaveLength(43);
        tokens.ExpiraEm.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
        tokens.RenovacaoExpiraEm.Should().BeCloseTo(DateTimeOffset.UtcNow.AddDays(14), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Renovar_devolve_um_par_novo_que_funciona_e_o_antigo_deixa_de_valer()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var primeiro = await LoginAsync(cliente);

        var resposta = await RenovarAsync(cliente, primeiro.Renovacao);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var novo = await TokensAsync(resposta);
        novo.Renovacao.Should().NotBe(primeiro.Renovacao);
        var pedidos = await api.CreateClient().ComToken(novo.Acesso).GetAsync(RotaPedidos, Cancelamento);
        pedidos.StatusCode.Should().Be(HttpStatusCode.OK);
        (await RenovarAsync(cliente, novo.Renovacao)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reutilizar_um_token_encerra_a_sessao_e_o_mais_recente_tambem_para()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        await using var api = Api(banco.NovoBanco(), logs: logs);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var primeiro = await LoginAsync(cliente);
        var novo = await TokensAsync(await RenovarAsync(cliente, primeiro.Renovacao));

        var reutilizacao = await RenovarAsync(cliente, primeiro.Renovacao);
        var maisRecente = await RenovarAsync(cliente, novo.Renovacao);

        (await AssinaturaAsync(reutilizacao)).Should().Be(Invalida);
        (await AssinaturaAsync(maisRecente)).Should().Be(Invalida);
        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3010);
    }

    [Fact]
    public async Task Inexistente_expirado_e_revogado_tem_o_mesmo_401()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var relogio = new RelogioAjustavel();
        await using var api = Api(banco.NovoBanco(), relogio: relogio);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var revogado = await LoginAsync(cliente);
        (await LogoutAsync(cliente, revogado.Renovacao)).EnsureSuccessStatusCode();
        var expirado = await LoginAsync(cliente);

        (await AssinaturaAsync(await RenovarAsync(cliente, "token-que-nao-existe"))).Should().Be(Invalida);
        (await AssinaturaAsync(await RenovarAsync(cliente, revogado.Renovacao))).Should().Be(Invalida);
        relogio.Agora += TimeSpan.FromDays(14) + TimeSpan.FromSeconds(1);
        (await AssinaturaAsync(await RenovarAsync(cliente, expirado.Renovacao))).Should().Be(Invalida);
    }

    [Fact]
    public async Task Perfil_alterado_depois_do_login_aparece_no_token_novo()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var tokens = await LoginAsync(cliente);
        await using (var escopo = api.Services.CreateAsyncScope())
        {
            var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            var usuario = await usuarios.FindByEmailAsync(Email);
            (await usuarios.AddToRoleAsync(usuario!, nameof(PerfilUsuario.Admin))).Succeeded.Should().BeTrue();
        }

        var novo = await TokensAsync(await RenovarAsync(cliente, tokens.Renovacao));

        tokens.Perfis.Should().Equal(nameof(PerfilUsuario.Cliente));
        novo.Perfis.Should().BeEquivalentTo(nameof(PerfilUsuario.Cliente), nameof(PerfilUsuario.Admin));
    }

    [Fact]
    public async Task Conta_bloqueada_por_erros_de_senha_continua_renovando()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var tokens = await LoginAsync(cliente);
        for (var i = 0; i < 5; i++)
        {
            await TentarLoginAsync(cliente, senha: "SenhaErrada1");
        }

        (await TentarLoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a conta está bloqueada");
        (await RenovarAsync(cliente, tokens.Renovacao)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cinquenta_renovacoes_simultaneas_tem_exatamente_uma_aceita()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var tokens = await LoginAsync(cliente);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => RenovarAsync(cliente, tokens.Renovacao)));

        respostas.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        respostas.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(49);
    }

    [Fact]
    public async Task A_sessao_nao_passa_de_30_dias_mesmo_renovando()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var relogio = new RelogioAjustavel();
        var inicio = relogio.Agora;
        await using var api = Api(banco.NovoBanco(), relogio: relogio);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var tokens = await LoginAsync(cliente);
        relogio.Agora = inicio + TimeSpan.FromDays(10);
        tokens = await TokensAsync(await RenovarAsync(cliente, tokens.Renovacao));
        relogio.Agora = inicio + TimeSpan.FromDays(20);
        tokens = await TokensAsync(await RenovarAsync(cliente, tokens.Renovacao));

        tokens.RenovacaoExpiraEm.Should().BeCloseTo(inicio + TimeSpan.FromDays(30), TimeSpan.FromSeconds(1));
        relogio.Agora = inicio + TimeSpan.FromDays(30) + TimeSpan.FromSeconds(1);
        (await AssinaturaAsync(await RenovarAsync(cliente, tokens.Renovacao))).Should().Be(Invalida);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"tokenRenovacao":""}""")]
    public async Task Sem_o_token_a_resposta_e_400(string corpo)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());

        var resposta = await api.CreateClient().PostAsync(RotaRenovacao, new StringContent(corpo, System.Text.Encoding.UTF8, "application/json"), Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadAsStringAsync(Cancelamento)).Should().Contain("O token de renovação é obrigatório.");
    }

    [Fact]
    public async Task Token_longo_demais_e_recusado_antes_de_calcular()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());

        var resposta = await RenovarAsync(api.CreateClient(), new string('a', 129));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Acima_do_limite_a_resposta_e_429()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco(), new Dictionary<string, string?> { ["LimiteRequisicoes:Renovacao:Limite"] = "1" });
        var cliente = api.CreateClient();

        (await RenovarAsync(cliente, "a")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var barrada = await RenovarAsync(cliente, "a");

        barrada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        barrada.Headers.RetryAfter.Should().NotBeNull();
    }
}
