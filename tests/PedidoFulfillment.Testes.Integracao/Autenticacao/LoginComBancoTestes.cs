using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>
/// User Story 2 com SQL Server real: login, resposta genérica, bloqueio e uso do token.
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class LoginComBancoTestes(BancoDadosTeste banco)
{
    private const string Registro = "/api/v1/auth/register";
    private const string Login = "/api/v1/auth/login";
    private const string Email = "Maria@Exemplo.com.br";
    private const string Senha = "SenhaForte1";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("maria@exemplo.com.br")]
    [InlineData("  MARIA@Exemplo.com.br ")]
    public async Task Login_aceita_o_email_com_outra_caixa_ou_espacos(string email)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = RegistroComBancoTestes.Api(banco.NovoBanco());
        var cliente = await ClienteComContaAsync(api);

        var resposta = await cliente.PostAsJsonAsync(Login, new { email, senha = Senha }, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento);
        corpo.GetProperty("tipoToken").GetString().Should().Be("Bearer");
        corpo.GetProperty("perfis").EnumerateArray().Select(p => p.GetString()).Should().Equal("Cliente");
        corpo.GetProperty("expiraEm").GetDateTimeOffset().Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(60), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Senha_errada_e_email_inexistente_recebem_a_mesma_resposta()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = RegistroComBancoTestes.Api(banco.NovoBanco());
        var cliente = await ClienteComContaAsync(api);

        var senhaErrada = await Recusa(cliente, Email, "SenhaErrada1");
        var emailInexistente = await Recusa(cliente, "ninguem@exemplo.com.br", Senha);

        senhaErrada.Should().Be(emailInexistente);
        senhaErrada.Titulo.Should().Be("Credenciais inválidas.");
    }

    [Fact]
    public async Task Cinco_erros_seguidos_bloqueiam_a_conta_por_quinze_minutos()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        var logs = new CapturaLogs();
        await using var api = RegistroComBancoTestes.Api(stringConexao, logs);
        var cliente = await ClienteComContaAsync(api);
        var generica = await Recusa(cliente, Email, "SenhaErrada1");
        for (var tentativa = 2; tentativa <= 5; tentativa++)
        {
            await Recusa(cliente, Email, "SenhaErrada1");
        }

        (await Recusa(cliente, Email, Senha)).Should().Be(generica, "durante o bloqueio até a senha correta é recusada");
        var fimBloqueio = await BancoDadosTeste.ConsultarAsync<DateTimeOffset>(stringConexao, "SELECT LockoutEnd FROM Usuarios");
        fimBloqueio.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));

        // Em vez de esperar 15 minutos, o fim do bloqueio vai para o passado (research R-11).
        await BancoDadosTeste.ConsultarAsync<object>(stringConexao,
            "UPDATE Usuarios SET LockoutEnd = DATEADD(MINUTE, -1, SYSDATETIMEOFFSET()); SELECT 1");
        (await cliente.PostAsJsonAsync(Login, new { email = Email, senha = Senha }, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        logs.Registros.Select(r => r.Evento.Name).Should().Contain(["LoginRecusado", "ContaBloqueada", "LoginRealizado"]);
        RegistroComBancoTestes.DeveSerSemDadosSensiveis(logs, "SenhaErrada1");
    }

    [Fact]
    public async Task Login_correto_zera_a_contagem_de_erros()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = RegistroComBancoTestes.Api(banco.NovoBanco());
        var cliente = await ClienteComContaAsync(api);

        for (var rodada = 0; rodada < 2; rodada++)
        {
            for (var tentativa = 0; tentativa < 4; tentativa++)
            {
                await Recusa(cliente, Email, "SenhaErrada1");
            }

            (await cliente.PostAsJsonAsync(Login, new { email = Email, senha = Senha }, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Senha_com_espacos_so_funciona_identica()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = RegistroComBancoTestes.Api(banco.NovoBanco());
        var cliente = api.CreateClient();
        const string senhaComEspacos = " Senha Forte1 ";
        (await cliente.PostAsJsonAsync(Registro, new { nome = "Maria Silva", email = Email, senha = senhaComEspacos }, Cancelamento))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        (await cliente.PostAsJsonAsync(Login, new { email = Email, senha = senhaComEspacos }, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await cliente.PostAsJsonAsync(Login, new { email = Email, senha = senhaComEspacos.Trim() }, Cancelamento)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_do_login_e_aceito_por_uma_rota_protegida()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        await using var api = RegistroComBancoTestes.Api(banco.NovoBanco(), logs);
        var cliente = await ClienteComContaAsync(api);
        var token = await TokenAsync(cliente, Email, Senha);

        var resposta = await api.CreateClient().ComToken(token).SendAsync(Exemplos.CriarPedido(), Cancelamento);

        // Passou pela autorização e chegou ao serviço de pedidos (REQ-05); o produto de exemplo
        // não existe no banco, então a regra responde 422.
        resposta.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized).And.NotBe(HttpStatusCode.Forbidden);
        RegistroComBancoTestes.DeveSerSemDadosSensiveis(logs, token);
    }

    [Fact]
    public async Task Noventa_e_cinco_por_cento_dos_logins_levam_menos_de_um_segundo()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = RegistroComBancoTestes.Api(banco.NovoBanco());
        var cliente = await ClienteComContaAsync(api);
        await TokenAsync(cliente, Email, Senha);

        var duracoes = new List<TimeSpan>();
        for (var i = 0; i < 20; i++)
        {
            var cronometro = Stopwatch.StartNew();
            await TokenAsync(cliente, Email, Senha);
            duracoes.Add(cronometro.Elapsed);
        }

        duracoes.Order().ElementAt(18).Should().BeLessThan(TimeSpan.FromSeconds(1));
    }

    internal static async Task<HttpClient> ClienteComContaAsync(FabricaApi api)
    {
        var cliente = api.CreateClient();
        var registro = await cliente.PostAsJsonAsync(Registro, new { nome = "Maria Silva", email = Email, senha = Senha }, Cancelamento);
        registro.StatusCode.Should().Be(HttpStatusCode.Created);
        return cliente;
    }

    internal static async Task<string> TokenAsync(HttpClient cliente, string email, string senha)
    {
        var resposta = await cliente.PostAsJsonAsync(Login, new { email, senha }, Cancelamento);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento)).GetProperty("tokenAcesso").GetString()!;
    }

    private static async Task<(HttpStatusCode Status, string? Titulo, string? Detalhe)> Recusa(HttpClient cliente, string email, string senha)
    {
        var resposta = await cliente.PostAsJsonAsync(Login, new { email, senha }, Cancelamento);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento);
        return (resposta.StatusCode, corpo.GetProperty("title").GetString(), corpo.GetProperty("detail").GetString());
    }
}
