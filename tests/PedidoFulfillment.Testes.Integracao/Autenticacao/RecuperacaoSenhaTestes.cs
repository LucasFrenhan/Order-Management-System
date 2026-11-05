using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Infrastructure.Identidade;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Emails;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Autenticacao.CenarioAutenticacao;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>User Story 3 com SQL Server real e enviador falso: recuperar a conta (REQ-24).</summary>
public sealed class RecuperacaoSenhaTestes(BancoDadosTeste banco)
{
    private const string Invalido = "400|Token inválido.|O token é inválido ou expirou. Peça uma nova recuperação.|TOKEN_INVALIDO";

    private static async Task<string> TokenPorEmailAsync(FabricaApi api, string stringConexao, EnviadorEmailFalso enviador, HttpClient cliente, string email = Email)
    {
        (await PedirRecuperacaoAsync(cliente, email)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await RodadaEmailsAsync(api, stringConexao);
        return ProcessadorEmailsTestes.TokenDe(enviador.Mensagens[^1]);
    }

    [Fact]
    public async Task Com_conta_responde_202_e_envia_e_sem_conta_o_mesmo_202_sem_enviar()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        await using var api = ApiComEmail(conexao, enviador);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        await RodadaEmailsAsync(api, conexao);
        enviador.Mensagens.Clear();

        var comConta = await PedirRecuperacaoAsync(cliente);
        await RodadaEmailsAsync(api, conexao);
        var enviadosComConta = enviador.Mensagens.Count;
        var semConta = await PedirRecuperacaoAsync(cliente, "ninguem@exemplo.com.br");
        await RodadaEmailsAsync(api, conexao);

        comConta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        semConta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await comConta.Content.ReadAsStringAsync(Cancelamento)).Should().BeEmpty();
        (await semConta.Content.ReadAsStringAsync(Cancelamento)).Should().BeEmpty();
        enviadosComConta.Should().Be(1);
        enviador.Mensagens.Should().ContainSingle("nenhum e-mail para quem não tem conta");
    }

    [Fact]
    public async Task Redefinir_muda_a_senha_invalida_o_token_encerra_as_sessoes_e_avisa()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        var logs = new CapturaLogs();
        await using var api = ApiComEmail(conexao, enviador, logs: logs);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var sessao = await LoginAsync(cliente);
        var outra = await LoginAsync(cliente);
        var token = await TokenPorEmailAsync(api, conexao, enviador, cliente);

        var redefinicao = await RedefinirAsync(cliente, token);

        redefinicao.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await TentarLoginAsync(cliente, senha: NovaSenha)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await TentarLoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RenovarAsync(cliente, sessao.Renovacao)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RenovarAsync(cliente, outra.Renovacao)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AssinaturaAsync(await RedefinirAsync(cliente, token, "TerceiraSenha3"))).Should().Be(Invalido);
        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3014);

        await RodadaEmailsAsync(api, conexao);
        enviador.Mensagens[^1].Assunto.Should().Be("Sua senha foi redefinida");
    }

    [Fact]
    public async Task Expirado_usado_e_inexistente_tem_a_mesma_resposta_e_a_senha_nao_muda()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        var relogio = new RelogioAjustavel();
        await using var api = ApiComEmail(conexao, enviador, relogio: relogio);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var token = await TokenPorEmailAsync(api, conexao, enviador, cliente);

        (await AssinaturaAsync(await RedefinirAsync(cliente, "token-que-nao-existe"))).Should().Be(Invalido);
        relogio.Agora += TimeSpan.FromMinutes(31);
        (await AssinaturaAsync(await RedefinirAsync(cliente, token))).Should().Be(Invalido);

        (await TentarLoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.OK, "a senha continua a mesma");
    }

    [Fact]
    public async Task So_o_token_mais_recente_vale()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        await using var api = ApiComEmail(conexao, enviador);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var primeiro = await TokenPorEmailAsync(api, conexao, enviador, cliente);
        var segundo = await TokenPorEmailAsync(api, conexao, enviador, cliente);

        (await AssinaturaAsync(await RedefinirAsync(cliente, primeiro))).Should().Be(Invalido);
        (await RedefinirAsync(cliente, segundo)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Redefinir_zera_o_bloqueio_e_a_contagem_e_confirma_o_email()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        await using var api = ApiComEmail(conexao, enviador);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        for (var i = 0; i < 5; i++)
        {
            await TentarLoginAsync(cliente, senha: "SenhaErrada1");
        }

        var token = await TokenPorEmailAsync(api, conexao, enviador, cliente);
        (await RedefinirAsync(cliente, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var escopo = api.Services.CreateAsyncScope();
        var usuario = await escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByEmailAsync(Email);
        usuario!.AccessFailedCount.Should().Be(0);
        usuario.LockoutEnd.Should().BeNull();
        usuario.EmailConfirmed.Should().BeTrue();
        (await TentarLoginAsync(cliente, senha: NovaSenha)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("curta1A", "A senha deve ter entre 8 e 128 caracteres.")]
    [InlineData("semdigitoNem", "A senha deve ter ao menos uma letra maiúscula, uma letra minúscula e um dígito.")]
    public async Task Senha_fora_das_regras_responde_400_sem_consumir_o_token(string senha, string mensagem)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        await using var api = ApiComEmail(conexao, enviador);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var token = await TokenPorEmailAsync(api, conexao, enviador, cliente);

        var resposta = await RedefinirAsync(cliente, token, senha);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var corpo = await resposta.Content.ReadAsStringAsync(Cancelamento);
        corpo.Should().Contain("novaSenha").And.Contain(mensagem).And.NotContain(senha);
        (await RedefinirAsync(cliente, token)).StatusCode.Should().Be(HttpStatusCode.NoContent, "o token continua valendo");
    }

    [Fact]
    public async Task Vinte_redefinicoes_simultaneas_com_o_mesmo_token_tem_uma_so_aceita()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        await using var api = ApiComEmail(conexao, enviador);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var token = await TokenPorEmailAsync(api, conexao, enviador, cliente);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => RedefinirAsync(cliente, token)));

        respostas.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        respostas.Count(r => r.StatusCode == HttpStatusCode.BadRequest).Should().Be(19);
    }

    [Fact]
    public async Task Falha_na_redefinicao_desfaz_tudo_inclusive_o_consumo_do_token()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        await using var api = ApiComEmail(conexao, enviador);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        var token = await TokenPorEmailAsync(api, conexao, enviador, cliente);
        // Uma restrição que recusa qualquer linha: a solicitação de aviso, último passo, falha.
        await using (var escopo = api.Services.CreateAsyncScope())
        {
            var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
            await contexto.Database.ExecuteSqlRawAsync("ALTER TABLE SolicitacoesEmail ADD CONSTRAINT CK_Falha CHECK (Tentativas < 0)", Cancelamento);
        }

        (await RedefinirAsync(cliente, token)).StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        (await TentarLoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.OK, "a senha não mudou");

        await using (var escopo = api.Services.CreateAsyncScope())
        {
            var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
            await contexto.Database.ExecuteSqlRawAsync("ALTER TABLE SolicitacoesEmail DROP CONSTRAINT CK_Falha", Cancelamento);
        }

        (await RedefinirAsync(cliente, token)).StatusCode.Should().Be(HttpStatusCode.NoContent, "o token não foi gasto");
    }

    [Fact]
    public async Task E_mail_mal_formado_responde_400()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());

        var resposta = await PedirRecuperacaoAsync(api.CreateClient(), "nao-e-email");

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task O_quarto_pedido_do_mesmo_email_na_hora_e_barrado_com_retry_after()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco(), new Dictionary<string, string?>
        {
            ["LimiteRequisicoes:RecuperacaoOrigemEmail:Limite"] = "3",
            ["LimiteRequisicoes:RecuperacaoOrigemEmail:JanelaSegundos"] = "3600"
        });
        var cliente = api.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            (await PedirRecuperacaoAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        }

        var barrada = await PedirRecuperacaoAsync(cliente);

        barrada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        barrada.Headers.RetryAfter.Should().NotBeNull();
        (await PedirRecuperacaoAsync(cliente, "outra@exemplo.com.br")).StatusCode.Should().Be(HttpStatusCode.Accepted, "o limite é por e-mail");
    }

    [Fact]
    public async Task Redefinicao_acima_do_limite_responde_429()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco(), new Dictionary<string, string?> { ["LimiteRequisicoes:Redefinicao:Limite"] = "1" });
        var cliente = api.CreateClient();

        (await RedefinirAsync(cliente, "a")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RedefinirAsync(cliente, "a")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
