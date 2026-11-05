using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Emails;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Emails;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Autenticacao.CenarioAutenticacao;

namespace PedidoFulfillment.Testes.Integracao.Emails;

/// <summary>Pelo SMTP real, com o MailKit e o Mailpit (REQ-24, SC-004, SC-007, FR-021).</summary>
public sealed partial class EnvioMailpitTestes(BancoDadosTeste banco, MailpitTeste mailpit) : IClassFixture<MailpitTeste>
{
    [GeneratedRegex(@"Token: (\S+)")]
    private static partial Regex ExpressaoToken();

    private OpcoesEmail Opcoes() => new()
    {
        Servidor = mailpit.Servidor,
        Porta = mailpit.Porta,
        Remetente = "oms@exemplo.local",
        Seguranca = OpcoesEmail.SegurancaNenhuma,
        TempoLimiteSegundos = 5
    };

    private static string EmailUnico() => $"cliente-{Guid.NewGuid():N}@exemplo.com.br";

    [Fact]
    public async Task Recuperacao_de_ponta_a_ponta_em_menos_de_2_minutos()
    {
        Assert.SkipUnless(banco.DockerDisponivel && mailpit.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var opcoes = Opcoes();
        var email = EmailUnico();
        await using var api = ApiComEmail(conexao, new EnviadorEmailSmtp(Options.Create(opcoes)));
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente, email);
        var inicio = Stopwatch.GetTimestamp();

        (await PedirRecuperacaoAsync(cliente, email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await RodadaEmailsAsync(api, conexao, opcoes);
        var capturado = (await mailpit.MensagensParaAsync(email)).Should().ContainSingle(m => m.Assunto == "Recuperação de senha").Subject;
        var token = ExpressaoToken().Match(capturado.Texto).Groups[1].Value;
        (await RedefinirAsync(cliente, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await TentarLoginAsync(cliente, email, NovaSenha)).StatusCode.Should().Be(HttpStatusCode.OK);

        Stopwatch.GetElapsedTime(inicio).Should().BeLessThan(TimeSpan.FromMinutes(2));
        await RodadaEmailsAsync(api, conexao, opcoes);
        (await mailpit.MensagensParaAsync(email)).Should().Contain(m => m.Assunto == "Sua senha foi redefinida");
    }

    [Fact]
    public async Task Com_o_servidor_fora_as_rotas_respondem_igual_e_o_email_chega_depois_da_volta()
    {
        Assert.SkipUnless(banco.DockerDisponivel && mailpit.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var relogio = new RelogioAjustavel();
        var logs = new CapturaLogs();
        var opcoes = Opcoes();
        var portaReal = opcoes.Porta;
        // Porta fechada: é o que o Worker vê com o servidor parado. A queda do contêiner está no quickstart (3.6).
        opcoes.Porta = 1;
        var email = EmailUnico();
        await using var api = ApiComEmail(conexao, new EnviadorEmailSmtp(Options.Create(opcoes)), logs: logs, relogio: relogio);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente, email);

        (await PedirRecuperacaoAsync(cliente, email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await TentarLoginAsync(cliente, email)).StatusCode.Should().Be(HttpStatusCode.OK);
        await RodadaEmailsAsync(api, conexao, opcoes);
        logs.Registros.Should().Contain(r => r.Evento.Id == 3017);
        logs.Textos.Should().NotContain(t => t.Contains(email));
        (await mailpit.MensagensParaAsync(email)).Should().BeEmpty();

        // O servidor volta. A espera máxima é de 2 min, dentro dos 5 min do SC-007.
        opcoes.Porta = portaReal;
        relogio.Agora += TimeSpan.FromMinutes(2);
        await RodadaEmailsAsync(api, conexao, opcoes);

        (await mailpit.MensagensParaAsync(email)).Should().ContainSingle(m => m.Assunto == "Recuperação de senha");
    }

    [Fact]
    public async Task Destino_mal_formado_e_recusa_permanente_sem_tentar_o_servidor()
    {
        Assert.SkipUnless(mailpit.DockerDisponivel, "Docker indisponível");
        var enviador = new EnviadorEmailSmtp(Options.Create(Opcoes()));

        var resultado = await enviador.EnviarAsync(new MensagemEmail("nao e um email @@", "Assunto", "Corpo"), Cancelamento);

        resultado.Should().Be(ResultadoEnvioEmail.RecusaPermanente);
    }

    [Fact]
    public async Task O_token_nao_aparece_no_log_do_envio()
    {
        Assert.SkipUnless(banco.DockerDisponivel && mailpit.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var logs = new CapturaLogs();
        var opcoes = Opcoes();
        var email = EmailUnico();
        await using var api = ApiComEmail(conexao, new EnviadorEmailSmtp(Options.Create(opcoes)), logs: logs);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente, email);
        await PedirRecuperacaoAsync(cliente, email);

        await RodadaEmailsAsync(api, conexao, opcoes);

        var token = ExpressaoToken().Match((await mailpit.MensagensParaAsync(email)).Single().Texto).Groups[1].Value;
        token.Should().NotBeEmpty();
        logs.Textos.Should().NotContain(t => t.Contains(token) || t.Contains(email));
        logs.Registros.Should().Contain(r => r.Evento.Id == 3016);
    }
}
