using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Autenticacao.CenarioAutenticacao;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>User Story 4 com SQL Server real: trocar a senha estando conectado (REQ-24).</summary>
public sealed class TrocaSenhaTestes(BancoDadosTeste banco)
{
    private static Task<HttpResponseMessage> TrocarAsync(HttpClient cliente, string senhaAtual = Senha, string novaSenha = NovaSenha) =>
        cliente.PostAsJsonAsync(RotaTroca, new { senhaAtual, novaSenha }, Cancelamento);

    private static async Task<(HttpClient Anonimo, HttpClient Conectado, Tokens Tokens)> ContaAsync(FabricaApi api)
    {
        var anonimo = api.CreateClient();
        await RegistrarAsync(anonimo);
        var tokens = await LoginAsync(anonimo);
        return (anonimo, api.CreateClient().ComToken(tokens.Acesso), tokens);
    }

    [Fact]
    public async Task Troca_responde_204_muda_a_senha_e_encerra_todas_as_sessoes()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        await using var api = Api(banco.NovoBanco(), logs: logs);
        var (anonimo, conectado, tokens) = await ContaAsync(api);
        var outra = await LoginAsync(anonimo);

        var resposta = await TrocarAsync(conectado);

        resposta.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await TentarLoginAsync(anonimo, senha: NovaSenha)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TentarLoginAsync(anonimo)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RenovarAsync(anonimo, tokens.Renovacao)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "inclusive a do dispositivo que trocou");
        (await RenovarAsync(anonimo, outra.Renovacao)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3012);
        logs.Textos.Should().NotContain(t => t.Contains(Senha) || t.Contains(NovaSenha));
    }

    [Fact]
    public async Task O_token_de_acesso_continua_valendo_ate_expirar()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var (_, conectado, _) = await ContaAsync(api);

        await TrocarAsync(conectado);

        (await conectado.GetAsync("/api/v1/pedidos", Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Senha_atual_errada_responde_400_no_campo_e_chega_ao_bloqueio_sem_revela_lo()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        await using var api = Api(banco.NovoBanco(), logs: logs);
        var (anonimo, conectado, _) = await ContaAsync(api);
        var respostas = new List<string>();

        for (var i = 0; i < 6; i++)
        {
            var resposta = await TrocarAsync(conectado, "SenhaErrada1");
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var corpo = await resposta.Content.ReadAsStringAsync(Cancelamento);
            corpo.Should().Contain("senhaAtual").And.Contain("A senha atual está incorreta.");
            respostas.Add(corpo);
        }

        // Bloqueada, a senha certa recebe a mesma resposta (FR-012).
        var comASenhaCerta = await TrocarAsync(conectado);
        comASenhaCerta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await comASenhaCerta.Content.ReadAsStringAsync(Cancelamento)).Should().Contain("A senha atual está incorreta.");
        (await TentarLoginAsync(anonimo)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a conta foi bloqueada");
        logs.Registros.Count(r => r.Evento.Id == 3013).Should().Be(7);
    }

    [Theory]
    [InlineData("curta1A", "A senha deve ter entre 8 e 128 caracteres.")]
    [InlineData("semdigitoNem", "A senha deve ter ao menos uma letra maiúscula, uma letra minúscula e um dígito.")]
    [InlineData(Senha, "A nova senha deve ser diferente da atual.")]
    public async Task Senha_nova_fora_das_regras_ou_igual_responde_400_sem_mudar_nada(string novaSenha, string mensagem)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var (anonimo, conectado, tokens) = await ContaAsync(api);

        var resposta = await TrocarAsync(conectado, novaSenha: novaSenha);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadAsStringAsync(Cancelamento)).Should().Contain("novaSenha").And.Contain(mensagem);
        (await TentarLoginAsync(anonimo)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RenovarAsync(anonimo, tokens.Renovacao)).StatusCode.Should().Be(HttpStatusCode.OK, "a sessão continua");
    }

    [Fact]
    public async Task Sem_token_e_com_token_invalido_a_resposta_e_401()
    {
        await using var api = new FabricaApi();

        (await TrocarAsync(api.CreateClient())).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a rota não é pública");
        (await TrocarAsync(api.CreateClient().ComToken("token-invalido"))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await TrocarAsync(api.CreateClient().ComToken(GeradorTokenTeste.Gerar(expirado: true, perfis: [PerfilUsuario.Cliente])))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Com_token_sem_perfil_a_resposta_e_403()
    {
        await using var api = new FabricaApi();

        (await TrocarAsync(api.CreateClient().ComToken())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(PerfilUsuario.Cliente)]
    [InlineData(PerfilUsuario.Admin)]
    [InlineData(PerfilUsuario.OperadorLogistica)]
    public async Task Todos_os_perfis_passam_pela_autorizacao(PerfilUsuario perfil)
    {
        await using var api = new FabricaApi();

        // Corpo inválido: chega à validação sem precisar de banco.
        var resposta = await TrocarAsync(api.CreateClient().ComToken(perfil), novaSenha: "x");

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
