using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Emails;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Autenticacao;
using PedidoFulfillment.Testes.Integracao.Emails;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Usuarios.CenarioGestaoUsuarios;

namespace PedidoFulfillment.Testes.Integracao.Usuarios;

/// <summary>User Story 2: a pessoa criada pelo Admin define a senha pelo convite (REQ-25, R-05).</summary>
public sealed class ConviteTestes(BancoDadosTeste banco)
{
    private const string Convidado = "ana@exemplo.com.br";
    private const string Invalido = "400|Token inválido.|O token é inválido ou expirou. Peça uma nova recuperação.|TOKEN_INVALIDO";

    private sealed record Ambiente(FabricaApi Api, string Conexao, EnviadorEmailFalso Enviador, RelogioAjustavel Relogio, CapturaLogs Logs, HttpClient Admin, HttpClient Anonimo) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Api.DisposeAsync();
    }

    private async Task<Ambiente> PrepararAsync()
    {
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        var logs = new CapturaLogs();
        var relogio = new RelogioAjustavel();
        var api = new FabricaApi
        {
            PrepararBanco = true,
            StringConexao = conexao,
            Logs = logs,
            Relogio = relogio,
            Configuracoes = AdminInicial,
            Servicos = s => s.AddSingleton<IEnviadorEmail>(enviador)
        };
        return new Ambiente(api, conexao, enviador, relogio, logs, await ClienteDeAsync(api), api.CreateClient());
    }

    private static async Task<Guid> ConvidarAsync(Ambiente a)
    {
        var resposta = await a.Admin.PostAsJsonAsync(Rota, new { nome = "Ana Lima", email = Convidado, perfil = "OperadorLogistica" }, Cancelamento);
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await CorpoAsync(resposta)).GetProperty("id").GetGuid();
    }

    private static async Task<string> TokenDoConviteAsync(Ambiente a, OpcoesEmail? opcoes = null)
    {
        await CenarioAutenticacao.RodadaEmailsAsync(a.Api, a.Conexao, opcoes);
        return ProcessadorEmailsTestes.TokenDe(a.Enviador.Mensagens[^1]);
    }

    [Fact]
    public async Task O_convite_chega_e_definir_a_senha_da_acesso_confirma_o_email_e_nao_avisa()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        var id = await ConvidarAsync(a);

        var token = await TokenDoConviteAsync(a);

        var convite = a.Enviador.Mensagens.Should().ContainSingle().Subject;
        convite.Destino.Should().Be(Convidado);
        convite.Assunto.Should().Be("Convite para acessar o sistema");
        convite.Corpo.Should().Contain("72 horas").And.Contain("recuperação de senha").And.NotContain("Admin Inicial");

        var definicao = await CenarioAutenticacao.RedefinirAsync(a.Anonimo, token);

        definicao.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CenarioAutenticacao.TentarLoginAsync(a.Anonimo, Convidado, CenarioAutenticacao.NovaSenha)).StatusCode.Should().Be(HttpStatusCode.OK);
        var conta = await LerContaAsync(a.Api, id);
        conta.EmailConfirmado.Should().BeTrue();
        conta.ConvitePendente.Should().BeFalse();
        a.Logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3019 && r.Texto.Contains(id.ToString()));
        a.Logs.Registros.Should().NotContain(r => r.Evento.Id == 3014);

        await CenarioAutenticacao.RodadaEmailsAsync(a.Api, a.Conexao);
        a.Enviador.Mensagens.Should().ContainSingle("quem aceitou o convite não recebe aviso de senha redefinida");
    }

    [Fact]
    public async Task Com_o_acesso_o_operador_usa_a_operacao_e_recebe_403_na_gestao()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        await ConvidarAsync(a);
        var token = await TokenDoConviteAsync(a);
        await CenarioAutenticacao.RedefinirAsync(a.Anonimo, token);

        var operador = await ClienteDeAsync(a.Api, Convidado, CenarioAutenticacao.NovaSenha);

        (await operador.GetAsync("/api/v1/pedidos", Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await operador.GetAsync(Rota, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Token_vencido_usado_ou_inexistente_recebe_a_mesma_resposta()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        var id = await ConvidarAsync(a);
        var token = await TokenDoConviteAsync(a);

        (await CenarioAutenticacao.AssinaturaAsync(await CenarioAutenticacao.RedefinirAsync(a.Anonimo, "nao-existe"))).Should().Be(Invalido);
        var relogioOriginal = a.Relogio.Agora;
        a.Relogio.Agora = relogioOriginal.AddHours(73);
        (await CenarioAutenticacao.AssinaturaAsync(await CenarioAutenticacao.RedefinirAsync(a.Anonimo, token))).Should().Be(Invalido);
        (await LerContaAsync(a.Api, id)).ConvitePendente.Should().BeTrue();

        a.Relogio.Agora = relogioOriginal;
        (await CenarioAutenticacao.RedefinirAsync(a.Anonimo, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CenarioAutenticacao.AssinaturaAsync(await CenarioAutenticacao.RedefinirAsync(a.Anonimo, token, "MaisUmaSenha3"))).Should().Be(Invalido);
    }

    [Fact]
    public async Task Senha_fora_das_regras_nao_gasta_o_token()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        await ConvidarAsync(a);
        var token = await TokenDoConviteAsync(a);

        (await CenarioAutenticacao.RedefinirAsync(a.Anonimo, token, "curta1A")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await CenarioAutenticacao.RedefinirAsync(a.Anonimo, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Login_sem_senha_e_o_401_de_sempre_e_nao_conta_para_o_bloqueio()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        var id = await ConvidarAsync(a);
        var inexistente = await CenarioAutenticacao.AssinaturaAsync(await CenarioAutenticacao.TentarLoginAsync(a.Anonimo, "ninguem@exemplo.com.br", SenhaPadrao));

        for (var i = 0; i < 6; i++)
        {
            (await CenarioAutenticacao.AssinaturaAsync(await CenarioAutenticacao.TentarLoginAsync(a.Anonimo, Convidado, SenhaPadrao))).Should().Be(inexistente);
        }

        await using var escopo = a.Api.Services.CreateAsyncScope();
        var conta = await escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>().Users.AsNoTracking().SingleAsync(u => u.Id == id, Cancelamento);
        conta.AccessFailedCount.Should().Be(0);
        conta.LockoutEnd.Should().BeNull();
    }

    [Fact]
    public async Task Convite_vencido_e_resolvido_pela_recuperacao_de_senha()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        var id = await ConvidarAsync(a);
        await TokenDoConviteAsync(a);
        a.Relogio.Agora = a.Relogio.Agora.AddHours(73);

        (await CenarioAutenticacao.PedirRecuperacaoAsync(a.Anonimo, Convidado)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await CenarioAutenticacao.RodadaEmailsAsync(a.Api, a.Conexao);
        var token = ProcessadorEmailsTestes.TokenDe(a.Enviador.Mensagens[^1]);

        (await CenarioAutenticacao.RedefinirAsync(a.Anonimo, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CenarioAutenticacao.TentarLoginAsync(a.Anonimo, Convidado, CenarioAutenticacao.NovaSenha)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LerContaAsync(a.Api, id)).ConvitePendente.Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Convite_e_recuperacao_ao_mesmo_tempo_o_primeiro_invalida_o_outro(bool conviteAntes)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        await ConvidarAsync(a);
        var convite = await TokenDoConviteAsync(a);
        await CenarioAutenticacao.PedirRecuperacaoAsync(a.Anonimo, Convidado);
        await CenarioAutenticacao.RodadaEmailsAsync(a.Api, a.Conexao);
        var recuperacao = ProcessadorEmailsTestes.TokenDe(a.Enviador.Mensagens[^1]);
        var (primeiro, segundo) = conviteAntes ? (convite, recuperacao) : (recuperacao, convite);

        (await CenarioAutenticacao.RedefinirAsync(a.Anonimo, primeiro)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await CenarioAutenticacao.AssinaturaAsync(await CenarioAutenticacao.RedefinirAsync(a.Anonimo, segundo, "MaisUmaSenha3"))).Should().Be(Invalido);
    }

    [Fact]
    public async Task Convite_de_conta_que_ja_tem_senha_e_descartado_sem_enviar()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        await ContaAsync(a.Api, "comsenha@exemplo.com.br");
        await using (var escopo = a.Api.Services.CreateAsyncScope())
        {
            await escopo.ServiceProvider.GetRequiredService<IFilaEmails>().EnfileirarAsync(TipoEmail.ConviteUsuario, "comsenha@exemplo.com.br", Cancelamento);
        }

        await CenarioAutenticacao.RodadaEmailsAsync(a.Api, a.Conexao);

        a.Enviador.Mensagens.Should().BeEmpty();
    }

    [Fact]
    public async Task A_validade_conta_a_partir_do_envio()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        await ConvidarAsync(a);

        // O SMTP cai por 20 horas; o convite ainda chega com a validade inteira.
        a.Relogio.Agora = a.Relogio.Agora.AddHours(20);
        var token = await TokenDoConviteAsync(a, new OpcoesEmail { ValidadeConviteHoras = 1 });
        a.Relogio.Agora = a.Relogio.Agora.AddMinutes(59);

        (await CenarioAutenticacao.RedefinirAsync(a.Anonimo, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Solicitacao_com_mais_de_24_horas_sem_enviar_e_descartada_e_a_recuperacao_resolve()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        var id = await ConvidarAsync(a);
        a.Relogio.Agora = a.Relogio.Agora.AddHours(25);

        await CenarioAutenticacao.RodadaEmailsAsync(a.Api, a.Conexao);

        a.Enviador.Mensagens.Should().BeEmpty();
        a.Logs.Registros.Should().Contain(r => r.Evento.Id == 3018);
        (await LerContaAsync(a.Api, id)).ConvitePendente.Should().BeTrue();

        await CenarioAutenticacao.PedirRecuperacaoAsync(a.Anonimo, Convidado);
        await CenarioAutenticacao.RodadaEmailsAsync(a.Api, a.Conexao);
        (await CenarioAutenticacao.RedefinirAsync(a.Anonimo, ProcessadorEmailsTestes.TokenDe(a.Enviador.Mensagens[^1]))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_definicao_conta_para_o_limite_da_redefinicao()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = new FabricaApi
        {
            PrepararBanco = true,
            StringConexao = banco.NovoBanco(),
            Configuracoes = new Dictionary<string, string?> { ["LimiteRequisicoes:Redefinicao:Limite"] = "2" }
        };
        var cliente = api.CreateClient();

        (await CenarioAutenticacao.RedefinirAsync(cliente, "a")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CenarioAutenticacao.RedefinirAsync(cliente, "b")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await CenarioAutenticacao.RedefinirAsync(cliente, "c")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
