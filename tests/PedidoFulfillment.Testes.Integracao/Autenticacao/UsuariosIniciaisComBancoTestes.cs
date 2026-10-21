using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>
/// User Story 3 com SQL Server real: usuários iniciais Admin e OperadorLogistica (FR-012).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class UsuariosIniciaisComBancoTestes(BancoDadosTeste banco)
{
    internal const string EmailAdmin = "admin@oms.teste";
    internal const string SenhaAdmin = "AdminTeste2026";
    internal const string EmailOperador = "operador@oms.teste";
    internal const string SenhaOperador = "OperadorTeste2026";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    internal static Dictionary<string, string?> Configuracao(string senhaAdmin = SenhaAdmin) => new()
    {
        ["UsuariosIniciais:0:Nome"] = "Administrador Teste",
        ["UsuariosIniciais:0:Email"] = EmailAdmin,
        ["UsuariosIniciais:0:Senha"] = senhaAdmin,
        ["UsuariosIniciais:0:Perfil"] = "Admin",
        ["UsuariosIniciais:1:Nome"] = "Operador Teste",
        ["UsuariosIniciais:1:Email"] = EmailOperador,
        ["UsuariosIniciais:1:Senha"] = SenhaOperador,
        ["UsuariosIniciais:1:Perfil"] = "OperadorLogistica"
    };

    internal static FabricaApi Api(string stringConexao, Dictionary<string, string?>? configuracoes = null) =>
        new() { PrepararBanco = true, StringConexao = stringConexao, Configuracoes = configuracoes ?? Configuracao() };

    [Fact]
    public async Task Admin_e_operador_configurados_entram_so_com_o_seu_perfil()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = api.CreateClient();

        (await PerfisDoLogin(cliente, EmailAdmin, SenhaAdmin)).Should().Equal("Admin");
        (await PerfisDoLogin(cliente, EmailOperador, SenhaOperador)).Should().Equal("OperadorLogistica");
    }

    [Fact]
    public async Task Reiniciar_nao_duplica_nem_altera_a_senha()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using (var primeira = Api(stringConexao))
        {
            primeira.CreateClient().Dispose();
        }

        await using var segunda = Api(stringConexao, Configuracao(senhaAdmin: "OutraSenha2026"));
        var cliente = segunda.CreateClient();

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM Usuarios")).Should().Be(2);
        (await PerfisDoLogin(cliente, EmailAdmin, SenhaAdmin)).Should().Equal("Admin");
        (await cliente.PostAsJsonAsync("/api/v1/auth/login", new { email = EmailAdmin, senha = "OutraSenha2026" }, Cancelamento))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Email_ja_cadastrado_como_cliente_continua_so_cliente()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using (var semIniciais = Api(stringConexao, []))
        {
            (await semIniciais.CreateClient().PostAsJsonAsync("/api/v1/auth/register",
                new { nome = "Cliente Comum", email = EmailAdmin, senha = "ClienteSenha1" }, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        await using var comIniciais = Api(stringConexao);

        (await PerfisDoLogin(comIniciais.CreateClient(), EmailAdmin, "ClienteSenha1")).Should().Equal("Cliente");
    }

    [Fact]
    public async Task Sem_configuracao_nenhum_usuario_inicial_e_criado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao, []);

        api.CreateClient().Dispose();

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM Usuarios")).Should().Be(0);
    }

    internal static async Task<string?[]> PerfisDoLogin(HttpClient cliente, string email, string senha)
    {
        var resposta = await cliente.PostAsJsonAsync("/api/v1/auth/login", new { email, senha }, Cancelamento);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var corpo = await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento);
        return corpo.GetProperty("perfis").EnumerateArray().Select(p => p.GetString()).ToArray();
    }
}
