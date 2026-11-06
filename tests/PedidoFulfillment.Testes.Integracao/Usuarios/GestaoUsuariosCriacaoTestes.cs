using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Usuarios.CenarioGestaoUsuarios;

namespace PedidoFulfillment.Testes.Integracao.Usuarios;

/// <summary>User Story 2: criar um usuário interno (REQ-25).</summary>
public sealed class GestaoUsuariosCriacaoTestes(BancoDadosTeste banco)
{
    private static object Corpo(string email = "ana@exemplo.com.br", string nome = "Ana Lima", string perfil = "OperadorLogistica") =>
        new { nome, email, perfil };

    [Theory]
    [InlineData("OperadorLogistica")]
    [InlineData("Admin")]
    public async Task Cria_com_201_Location_e_sem_credencial_e_registra_a_auditoria(string perfil)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        await using var api = Api(banco.NovoBanco(), logs);
        var cliente = await ClienteDeAsync(api);
        var atorId = await IdDeAsync(api, EmailAdmin);

        var resposta = await cliente.PostAsJsonAsync(Rota, Corpo(perfil: perfil), Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        var conta = await CorpoAsync(resposta);
        var id = conta.GetProperty("id").GetGuid();
        resposta.Headers.Location!.ToString().Should().Be($"{Rota}/{id}");
        conta.GetProperty("perfis").EnumerateArray().Select(p => p.GetString()).Should().Equal(perfil);
        conta.GetProperty("situacao").GetString().Should().Be("Ativa");
        conta.GetProperty("emailConfirmado").GetBoolean().Should().BeFalse();
        conta.GetProperty("convitePendente").GetBoolean().Should().BeTrue();
        conta.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "nome", "email", "perfis", "situacao", "emailConfirmado", "convitePendente");

        var auditoria = logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3020).Subject;
        auditoria.Texto.Should().Contain(id.ToString()).And.Contain(atorId.ToString()).And.Contain(perfil);
        auditoria.Texto.Should().NotContain("ana@exemplo.com.br").And.NotContain("Ana Lima");
    }

    [Fact]
    public async Task Sem_token_e_401_e_outros_perfis_sao_403()
    {
        await using var api = new FabricaApi();

        (await api.CreateClient().PostAsJsonAsync(Rota, Corpo(), Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await api.CreateClient().ComToken(PerfilUsuario.Cliente).PostAsJsonAsync(Rota, Corpo(), Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.CreateClient().ComToken(PerfilUsuario.OperadorLogistica).PostAsJsonAsync(Rota, Corpo(), Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("Cliente", "perfil")]
    [InlineData("Auditor", "perfil")]
    [InlineData("admin", "perfil")]
    [InlineData("", "perfil")]
    public async Task Perfil_que_nao_e_interno_e_400(string perfil, string campo)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = await ClienteDeAsync(api);

        var resposta = await cliente.PostAsJsonAsync(Rota, Corpo(perfil: perfil), Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CorpoAsync(resposta)).GetProperty("erros").TryGetProperty(campo, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("A", "ana@exemplo.com.br", "nome")]
    [InlineData(" A ", "ana@exemplo.com.br", "nome")]
    [InlineData("Ana Lima", "nao-e-email", "email")]
    [InlineData("Ana Lima", "", "email")]
    public async Task Nome_ou_email_invalidos_sao_400_e_nao_criam_a_conta(string nome, string email, string campo)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = await ClienteDeAsync(api);

        var resposta = await cliente.PostAsJsonAsync(Rota, Corpo(email, nome), Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CorpoAsync(resposta)).GetProperty("erros").TryGetProperty(campo, out _).Should().BeTrue();
        (await CorpoAsync(await cliente.GetAsync($"{Rota}?perfil=OperadorLogistica", Cancelamento))).GetProperty("itens").GetArrayLength().Should().Be(0);
    }

    [Theory]
    [InlineData("ana@exemplo.com.br")]
    [InlineData("ANA@exemplo.com.br")]
    [InlineData("  ana@exemplo.com.br ")]
    public async Task Email_ja_cadastrado_e_409_sem_diferenciar_maiusculas_nem_espacos(string repetido)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = await ClienteDeAsync(api);
        (await cliente.PostAsJsonAsync(Rota, Corpo(), Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Created);

        await DeveRecusarAsync(await cliente.PostAsJsonAsync(Rota, Corpo(repetido), Cancelamento), HttpStatusCode.Conflict, CodigosErro.UsuarioEmailJaCadastrado);
    }

    [Fact]
    public async Task Criacoes_simultaneas_com_o_mesmo_email_dao_um_201_e_o_resto_409()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = await ClienteDeAsync(api);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => cliente.PostAsJsonAsync(Rota, Corpo(), Cancelamento)));

        respostas.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        respostas.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(7);
    }

    [Fact]
    public async Task Quem_cria_e_conferido_de_novo_dentro_do_lock()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        await using var escopo = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.CreateAsyncScope(api.Services);
        var gestor = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<PedidoFulfillment.Application.Usuarios.IGestorUsuarios>(escopo.ServiceProvider);

        var resultado = await gestor.CriarAsync(Guid.NewGuid(), "Ana Lima", "ana@exemplo.com.br", PerfilUsuario.Admin, Cancelamento);

        resultado.Falha.Should().Be(PedidoFulfillment.Application.Usuarios.RegrasGestaoUsuarios.SemPermissao);
    }
}
