using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Erros;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>
/// User Story 1 com SQL Server real: registro, unicidade do e-mail e armazenamento da senha.
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class RegistroComBancoTestes(BancoDadosTeste banco)
{
    private const string Registro = "/api/v1/auth/register";
    private const string Senha = "SenhaForte1";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static object Corpo(string email = "Maria@Exemplo.com.br", string nome = "  Maria Silva ") =>
        new { nome, email, senha = Senha };

    [Fact]
    public async Task Registro_valido_cria_a_conta_com_perfil_cliente()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        await using var api = Api(banco.NovoBanco(), logs);

        var resposta = await api.CreateClient().PostAsJsonAsync(Registro, Corpo(), Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento);
        corpo.GetProperty("id").GetGuid().Should().NotBeEmpty();
        corpo.GetProperty("nome").GetString().Should().Be("Maria Silva");
        corpo.GetProperty("email").GetString().Should().Be("Maria@Exemplo.com.br");
        corpo.GetProperty("perfis").EnumerateArray().Select(p => p.GetString()).Should().Equal("Cliente");
        corpo.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "nome", "email", "perfis");
        logs.Registros.Should().Contain(r => r.Evento.Name == "UsuarioRegistrado");
        DeveSerSemDadosSensiveis(logs);
    }

    [Theory]
    [InlineData("MARIA@exemplo.COM.br")]
    [InlineData("  Maria@Exemplo.com.br ")]
    public async Task Mesmo_email_com_outra_caixa_ou_espacos_responde_409(string outraForma)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var cliente = api.CreateClient();
        (await cliente.PostAsJsonAsync(Registro, Corpo(), Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Created);

        var resposta = await cliente.PostAsJsonAsync(Registro, Corpo(outraForma), Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var corpo = await FormatoErroTestes.DeveSerProblemDetails(resposta, 409, Registro);
        corpo.GetProperty("codigo").GetString().Should().Be("USUARIO_EMAIL_JA_CADASTRADO");
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM Usuarios")).Should().Be(1);
    }

    [Fact]
    public async Task Registros_simultaneos_com_o_mesmo_email_criam_uma_so_conta()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var cliente = api.CreateClient();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => cliente.PostAsJsonAsync(Registro, Corpo(), Cancelamento)));

        respostas.Select(r => r.StatusCode).Should().ContainSingle(s => s == HttpStatusCode.Created);
        respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(9);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM Usuarios")).Should().Be(1);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM UsuariosPerfis")).Should().Be(1);
    }

    [Fact]
    public async Task Corpo_invalido_responde_400_sem_criar_conta()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);

        var resposta = await api.CreateClient().PostAsJsonAsync(Registro, new { nome = "Maria", email = "invalido", senha = "fraca" }, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM Usuarios")).Should().Be(0);
    }

    [Fact]
    public async Task Senha_fica_guardada_so_como_hash()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);

        await api.CreateClient().PostAsJsonAsync(Registro, Corpo(), Cancelamento);

        var hash = await BancoDadosTeste.ConsultarAsync<string>(stringConexao, "SELECT PasswordHash FROM Usuarios");
        hash.Should().NotBeNullOrEmpty().And.NotContain(Senha);
    }

    [Fact]
    public async Task Conta_continua_existindo_depois_de_reiniciar_a_api()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using (var primeira = Api(stringConexao))
        {
            await primeira.CreateClient().PostAsJsonAsync(Registro, Corpo(), Cancelamento);
        }

        await using var segunda = Api(stringConexao);
        var resposta = await segunda.CreateClient().PostAsJsonAsync(Registro, Corpo(), Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    internal static FabricaApi Api(string stringConexao, CapturaLogs? logs = null) =>
        new() { PrepararBanco = true, StringConexao = stringConexao, Logs = logs };

    internal static void DeveSerSemDadosSensiveis(CapturaLogs logs, params string[] valoresProibidos)
    {
        foreach (var texto in logs.Textos)
        {
            texto.Should().NotContain(Senha).And.NotContainEquivalentOf("maria@exemplo.com.br");
            foreach (var valor in valoresProibidos)
            {
                texto.Should().NotContain(valor);
            }
        }
    }
}
