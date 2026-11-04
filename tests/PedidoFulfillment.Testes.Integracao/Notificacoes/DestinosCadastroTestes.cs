using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Autenticacao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Notificacoes;

/// <summary>
/// Cadastro e listagem dos destinos de webhook (REQ-18, US2 cenário 1; FR-007 a FR-010 e FR-026).
/// </summary>
public sealed class DestinosCadastroTestes(BancoDadosTeste banco)
{
    internal const string Rota = "/api/v1/notificacoes/destinos";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static Task<HttpResponseMessage> CadastrarAsync(HttpClient cliente, string url) =>
        cliente.PostAsJsonAsync(Rota, new { url }, Token);

    [Fact]
    public async Task Cliente_e_Admin_cadastram_e_o_segredo_aparece_so_no_cadastro()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var admin = await AdminAsync(api);

        var doCliente = await CadastrarAsync(cliente, "http://127.0.0.1:9/cliente");
        var global = await CadastrarAsync(admin, "http://127.0.0.1:9/global");

        doCliente.StatusCode.Should().Be(HttpStatusCode.Created);
        var corpo = await CorpoAsync(doCliente);
        var id = corpo.GetProperty("id").GetGuid();
        doCliente.Headers.Location!.ToString().Should().Be($"{Rota}/{id}");
        corpo.GetProperty("segredo").GetString().Should().StartWith("whsec_");
        (corpo.GetProperty("global").GetBoolean(), corpo.GetProperty("ativo").GetBoolean()).Should().Be((false, true));
        (await CorpoAsync(global)).GetProperty("global").GetBoolean().Should().BeTrue();

        var lista = await cliente.GetStringAsync(Rota, Token);
        lista.Should().Contain(id.ToString()).And.NotContain("segredo").And.NotContain("/global");
        var segredo = Convert.FromBase64String(corpo.GetProperty("segredo").GetString()!["whsec_".Length..]);
        var gravado = await BancoDadosTeste.ConsultarAsync<byte[]>(stringConexao, $"SELECT SegredoCifrado FROM DestinosNotificacao WHERE Id = '{id}'");
        gravado.Should().HaveCount(60);
        Encoding.Latin1.GetString(gravado).Should().NotContain(Encoding.Latin1.GetString(segredo), "o segredo fica cifrado (FR-009)");
    }

    [Fact]
    public async Task Sem_token_401_e_OperadorLogistica_403()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var operador = await OperadorAsync(api);

        (await CadastrarAsync(api.CreateClient(), "https://exemplo.com/x")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await api.CreateClient().GetAsync(Rota, Token)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CadastrarAsync(operador, "https://exemplo.com/x")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await operador.GetAsync(Rota, Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.CreateClient().ComToken().GetAsync(Rota, Token)).StatusCode.Should().Be(HttpStatusCode.Forbidden, "token sem perfil");
    }

    [Theory]
    [InlineData("")]
    [InlineData("/relativa")]
    [InlineData("ftp://exemplo.com/x")]
    [InlineData("https://usuario:senha@exemplo.com/x")]
    public async Task Url_fora_da_forma_responde_400(string url)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var (cliente, _, _) = await ClienteAsync(api);

        (await CadastrarAsync(cliente, url)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Url_longa_demais_responde_400()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var (cliente, _, _) = await ClienteAsync(api);

        (await CadastrarAsync(cliente, "https://exemplo.com/" + new string('a', 2048))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("http://exemplo.com/x", null)]
    [InlineData("https://127.0.0.1/x", "NOTIFICACAO_DESTINO_ENDERECO_PROIBIDO")]
    [InlineData("https://169.254.169.254/latest", "NOTIFICACAO_DESTINO_ENDERECO_PROIBIDO")]
    [InlineData("https://localhost/x", "NOTIFICACAO_DESTINO_ENDERECO_PROIBIDO")]
    [InlineData("https://host-que-nao-existe.invalid/x", "NOTIFICACAO_DESTINO_ENDERECO_PROIBIDO")]
    public async Task Sem_a_rede_local_http_e_endereco_interno_respondem_400(string url, string? codigo)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = new FabricaApi
        {
            PrepararBanco = true,
            StringConexao = stringConexao,
            Configuracoes = new Dictionary<string, string?>(UsuariosIniciaisComBancoTestes.Configuracao()) { ["Notificacoes:PermitirRedeLocal"] = "false" }
        };
        var (cliente, _, _) = await ClienteAsync(api);

        var resposta = await CadastrarAsync(cliente, url);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodigoAsync(resposta)).Should().Be(codigo);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM DestinosNotificacao")).Should().Be(0);
    }

    [Fact]
    public async Task Sexto_destino_do_Cliente_e_sexto_global_respondem_409()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var (cliente, _, _) = await ClienteAsync(api);
        var admin = await AdminAsync(api);

        for (var i = 0; i < 5; i++)
        {
            (await CadastrarAsync(cliente, $"http://127.0.0.1:9/c{i}")).StatusCode.Should().Be(HttpStatusCode.Created);
            (await CadastrarAsync(admin, $"http://127.0.0.1:9/g{i}")).StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var sextoCliente = await CadastrarAsync(cliente, "http://127.0.0.1:9/c5");
        var sextoGlobal = await CadastrarAsync(admin, "http://127.0.0.1:9/g5");

        sextoCliente.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(sextoCliente)).Should().Be("NOTIFICACAO_DESTINO_LIMITE");
        sextoGlobal.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cadastros_simultaneos_nao_passam_do_limite()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        for (var i = 0; i < 4; i++)
        {
            (await CadastrarAsync(cliente, $"http://127.0.0.1:9/c{i}")).EnsureSuccessStatusCode();
        }

        var respostas = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => CadastrarAsync(cliente, $"http://127.0.0.1:9/s{i}")));

        respostas.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(3);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM DestinosNotificacao")).Should().Be(5);
    }
}
