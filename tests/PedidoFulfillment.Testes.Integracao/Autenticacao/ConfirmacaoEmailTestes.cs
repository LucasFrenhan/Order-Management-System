using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Infrastructure.Identidade;
using PedidoFulfillment.Testes.Integracao.Emails;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Autenticacao.CenarioAutenticacao;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>User Story 5 com SQL Server real e enviador falso: confirmar o e-mail (REQ-24).</summary>
public sealed class ConfirmacaoEmailTestes(BancoDadosTeste banco)
{
    private const string Invalido = "400|Token inválido.|O token é inválido ou expirou. Peça um novo e-mail.|TOKEN_INVALIDO";

    private static Task<HttpResponseMessage> ConfirmarAsync(HttpClient cliente, string token) =>
        cliente.PostAsJsonAsync(RotaConfirmacao, new { token }, Cancelamento);

    private static Task<HttpResponseMessage> ReenviarAsync(HttpClient cliente, string email = Email) =>
        cliente.PostAsJsonAsync(RotaReenvio, new { email }, Cancelamento);

    private static async Task<bool> ConfirmadaAsync(FabricaApi api, string email = Email)
    {
        await using var escopo = api.Services.CreateAsyncScope();
        return (await escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByEmailAsync(email))!.EmailConfirmed;
    }

    [Fact]
    public async Task Registro_gera_o_email_e_o_token_confirma_a_conta()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        var logs = new CapturaLogs();
        await using var api = ApiComEmail(conexao, enviador, logs: logs);
        var cliente = api.CreateClient();

        var registro = await cliente.PostAsJsonAsync(RotaRegistro, new { nome = "Maria Silva", email = Email, senha = Senha }, Cancelamento);
        await RodadaEmailsAsync(api, conexao);

        registro.StatusCode.Should().Be(HttpStatusCode.Created);
        var corpo = await registro.Content.ReadFromJsonAsync<JsonElement>(Cancelamento);
        corpo.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "nome", "email", "perfis");
        var mensagem = enviador.Mensagens.Should().ContainSingle().Subject;
        mensagem.Assunto.Should().Be("Confirmação de e-mail");
        (await ConfirmadaAsync(api)).Should().BeFalse();

        (await ConfirmarAsync(cliente, ProcessadorEmailsTestes.TokenDe(mensagem))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ConfirmadaAsync(api)).Should().BeTrue();
        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3015);
    }

    [Fact]
    public async Task Registro_com_email_repetido_nao_deixa_solicitacao()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        await using var api = ApiComEmail(conexao, enviador);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        await RodadaEmailsAsync(api, conexao);
        enviador.Mensagens.Clear();

        var repetido = await cliente.PostAsJsonAsync(RotaRegistro, new { nome = "Maria Silva", email = Email, senha = Senha }, Cancelamento);
        await RodadaEmailsAsync(api, conexao);

        repetido.StatusCode.Should().Be(HttpStatusCode.Conflict);
        enviador.Mensagens.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalido_expirado_e_usado_tem_a_mesma_resposta()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        var relogio = new RelogioAjustavel();
        await using var api = ApiComEmail(conexao, enviador, relogio: relogio);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        await RodadaEmailsAsync(api, conexao);
        var token = ProcessadorEmailsTestes.TokenDe(enviador.Mensagens[^1]);

        (await AssinaturaAsync(await ConfirmarAsync(cliente, "token-que-nao-existe"))).Should().Be(Invalido);
        (await ConfirmarAsync(cliente, token)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await AssinaturaAsync(await ConfirmarAsync(cliente, token))).Should().Be(Invalido);

        await RegistrarAsync(cliente, "joao@exemplo.com.br");
        await RodadaEmailsAsync(api, conexao);
        var outro = ProcessadorEmailsTestes.TokenDe(enviador.Mensagens[^1]);
        relogio.Agora += TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1);
        (await AssinaturaAsync(await ConfirmarAsync(cliente, outro))).Should().Be(Invalido);
        (await ConfirmadaAsync(api, "joao@exemplo.com.br")).Should().BeFalse();
    }

    [Fact]
    public async Task Token_de_recuperacao_nao_confirma_o_email()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        await using var api = ApiComEmail(conexao, enviador);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        await RodadaEmailsAsync(api, conexao);
        await PedirRecuperacaoAsync(cliente);
        await RodadaEmailsAsync(api, conexao);

        (await AssinaturaAsync(await ConfirmarAsync(cliente, ProcessadorEmailsTestes.TokenDe(enviador.Mensagens[^1])))).Should().Be(Invalido);
    }

    [Fact]
    public async Task Reenvio_responde_o_mesmo_202_e_so_a_conta_nao_confirmada_recebe()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        await using var api = ApiComEmail(conexao, enviador);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        await RodadaEmailsAsync(api, conexao);
        var primeiroToken = ProcessadorEmailsTestes.TokenDe(enviador.Mensagens[^1]);
        enviador.Mensagens.Clear();

        var naoConfirmada = await ReenviarAsync(cliente);
        await RodadaEmailsAsync(api, conexao);
        var recebidos = enviador.Mensagens.Count;
        var novoToken = ProcessadorEmailsTestes.TokenDe(enviador.Mensagens[^1]);
        (await AssinaturaAsync(await ConfirmarAsync(cliente, primeiroToken))).Should().Be(Invalido, "o reenvio invalida o token anterior");
        (await ConfirmarAsync(cliente, novoToken)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        enviador.Mensagens.Clear();
        var confirmada = await ReenviarAsync(cliente);
        var semConta = await ReenviarAsync(cliente, "ninguem@exemplo.com.br");
        await RodadaEmailsAsync(api, conexao);

        recebidos.Should().Be(1);
        enviador.Mensagens.Should().BeEmpty();
        foreach (var resposta in new[] { naoConfirmada, confirmada, semConta })
        {
            resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
            (await resposta.Content.ReadAsStringAsync(Cancelamento)).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Recuperacao_concluida_confirma_o_email()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        await using var api = ApiComEmail(conexao, enviador);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        await RodadaEmailsAsync(api, conexao);
        await PedirRecuperacaoAsync(cliente);
        await RodadaEmailsAsync(api, conexao);

        (await RedefinirAsync(cliente, ProcessadorEmailsTestes.TokenDe(enviador.Mensagens[^1]))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ConfirmadaAsync(api)).Should().BeTrue();
    }

    [Fact]
    public async Task Login_sem_confirmacao_tem_a_mesma_resposta_de_uma_conta_confirmada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        await using var api = ApiComEmail(conexao, enviador);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        await RodadaEmailsAsync(api, conexao);

        var semConfirmar = await TentarLoginAsync(cliente);
        await ConfirmarAsync(cliente, ProcessadorEmailsTestes.TokenDe(enviador.Mensagens[^1]));
        var confirmado = await TentarLoginAsync(cliente);

        semConfirmar.StatusCode.Should().Be(HttpStatusCode.OK);
        confirmado.StatusCode.Should().Be(HttpStatusCode.OK);
        var campos = async (HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>(Cancelamento)).EnumerateObject().Select(p => p.Name).ToArray();
        (await campos(semConfirmar)).Should().BeEquivalentTo(await campos(confirmado));
    }

    [Fact]
    public async Task Conta_antiga_sem_sessao_nem_confirmacao_continua_entrando()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        await cenario.ContaAsync();
        await using var api = Api(cenario.StringConexao);

        var resposta = await TentarLoginAsync(api.CreateClient());

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await TokensAsync(resposta)).Renovacao.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(RotaConfirmacao, "LimiteRequisicoes:Confirmacao:Limite")]
    [InlineData(RotaReenvio, "LimiteRequisicoes:ReenvioOrigemEmail:Limite")]
    public async Task Acima_do_limite_a_resposta_e_429(string rota, string chave)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco(), new Dictionary<string, string?> { [chave] = "1" });
        var cliente = api.CreateClient();
        var corpo = new { token = "a", email = Email };

        await cliente.PostAsJsonAsync(rota, corpo, Cancelamento);
        var barrada = await cliente.PostAsJsonAsync(rota, corpo, Cancelamento);

        barrada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        barrada.Headers.RetryAfter.Should().NotBeNull();
    }
}
