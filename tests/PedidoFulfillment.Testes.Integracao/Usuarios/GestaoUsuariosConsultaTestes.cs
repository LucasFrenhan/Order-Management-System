using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Usuarios.CenarioGestaoUsuarios;

namespace PedidoFulfillment.Testes.Integracao.Usuarios;

/// <summary>User Story 1: consultar os usuários (REQ-25).</summary>
public sealed class GestaoUsuariosConsultaTestes(BancoDadosTeste banco)
{
    private static string[] Nomes(JsonElement pagina) =>
        [.. pagina.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("nome").GetString()!)];

    private static async Task<JsonElement> ListarAsync(HttpClient cliente, string consulta = "")
    {
        var resposta = await cliente.GetAsync($"{Rota}{consulta}", Cancelamento);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return await CorpoAsync(resposta);
    }

    [Fact]
    public async Task Admin_lista_e_consulta_sem_nenhum_dado_de_credencial()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = await ClienteDeAsync(api);
        var id = await ContaAsync(api, "ana@exemplo.com.br", "Ana Lima", PerfilUsuario.OperadorLogistica);

        var lista = await cliente.GetAsync(Rota, Cancelamento);
        var consulta = await cliente.GetAsync($"{Rota}/{id}", Cancelamento);

        lista.StatusCode.Should().Be(HttpStatusCode.OK);
        consulta.StatusCode.Should().Be(HttpStatusCode.OK);
        var conta = await CorpoAsync(consulta);
        conta.GetProperty("id").GetGuid().Should().Be(id);
        conta.GetProperty("nome").GetString().Should().Be("Ana Lima");
        conta.GetProperty("email").GetString().Should().Be("ana@exemplo.com.br");
        conta.GetProperty("perfis").EnumerateArray().Select(p => p.GetString()).Should().Equal("OperadorLogistica");
        conta.GetProperty("situacao").GetString().Should().Be("Ativa");
        conta.GetProperty("emailConfirmado").GetBoolean().Should().BeTrue();
        conta.GetProperty("convitePendente").GetBoolean().Should().BeFalse();

        foreach (var texto in new[] { await lista.Content.ReadAsStringAsync(Cancelamento), conta.GetRawText() })
        {
            foreach (var proibido in new[] { "senha", "hash", "securityStamp", "carimbo", "token", "AQAAAA" })
            {
                texto.Should().NotContainEquivalentOf(proibido);
            }
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("/0199f000-0000-7000-8000-000000000001")]
    public async Task Sem_token_e_401_e_outros_perfis_sao_403(string caminho)
    {
        await using var api = new FabricaApi();
        var rota = $"{Rota}{caminho}";

        (await api.CreateClient().GetAsync(rota, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await api.CreateClient().ComToken(PerfilUsuario.Cliente).GetAsync(rota, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await api.CreateClient().ComToken(PerfilUsuario.OperadorLogistica).GetAsync(rota, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Token_valido_de_quem_perdeu_o_Admin_ou_foi_desativado_recebe_403()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var perdeu = await ContaAsync(api, "perdeu@exemplo.com.br", perfis: [PerfilUsuario.Admin, PerfilUsuario.OperadorLogistica]);
        var desativado = await ContaAsync(api, "desativado@exemplo.com.br", perfis: PerfilUsuario.Admin);
        var clientePerdeu = await ClienteDeAsync(api, "perdeu@exemplo.com.br");
        var clienteDesativado = await ClienteDeAsync(api, "desativado@exemplo.com.br");
        (await clientePerdeu.GetAsync(Rota, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await clienteDesativado.GetAsync(Rota, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using (var escopo = api.Services.CreateAsyncScope())
        {
            var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
            var admin = await contexto.Roles.Where(r => r.Name == nameof(PerfilUsuario.Admin)).Select(r => r.Id).SingleAsync(Cancelamento);
            await contexto.UserRoles.Where(p => p.UserId == perdeu && p.RoleId == admin).ExecuteDeleteAsync(Cancelamento);
            await contexto.Users.Where(u => u.Id == desativado)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DesativadoEm, DateTimeOffset.UtcNow), Cancelamento);
        }

        (await clientePerdeu.GetAsync(Rota, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await clienteDesativado.GetAsync(Rota, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Filtros_se_combinam_e_a_busca_ignora_maiusculas_e_acentos()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = await ClienteDeAsync(api);
        await ContaAsync(api, "joao@exemplo.com.br", "João Lima", PerfilUsuario.OperadorLogistica);
        await ContaAsync(api, "bia.lima@exemplo.com.br", "Bia Souza", PerfilUsuario.Cliente);
        var desativado = await ContaAsync(api, "caio@exemplo.com.br", "Caio Lima", PerfilUsuario.OperadorLogistica);
        await using (var escopo = api.Services.CreateAsyncScope())
        {
            await escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>().Users.Where(u => u.Id == desativado)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DesativadoEm, DateTimeOffset.UtcNow), Cancelamento);
        }

        Nomes(await ListarAsync(cliente, "?perfil=OperadorLogistica")).Should().Equal("Caio Lima", "João Lima");
        Nomes(await ListarAsync(cliente, "?situacao=Desativada")).Should().Equal("Caio Lima");
        Nomes(await ListarAsync(cliente, "?perfil=OperadorLogistica&situacao=Ativa")).Should().Equal("João Lima");
        Nomes(await ListarAsync(cliente, "?busca=lima")).Should().Equal("Bia Souza", "Caio Lima", "João Lima");
        Nomes(await ListarAsync(cliente, "?busca=lima&perfil=Cliente")).Should().Equal("Bia Souza");
        Nomes(await ListarAsync(cliente, "?busca=joao")).Should().Equal("João Lima");
        Nomes(await ListarAsync(cliente, "?busca=JOAO")).Should().Equal("João Lima");
        Nomes(await ListarAsync(cliente, "?busca=%20%20lima%20")).Should().HaveCount(3);
    }

    [Theory]
    [InlineData("%25")]
    [InlineData("_")]
    public async Task Curinga_na_busca_e_tratado_como_texto(string curinga)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = await ClienteDeAsync(api);
        await ContaAsync(api, "ana@exemplo.com.br", "Ana Lima", PerfilUsuario.Cliente);

        Nomes(await ListarAsync(cliente, $"?busca={curinga}{curinga}")).Should().BeEmpty();
    }

    [Fact]
    public async Task Paginas_para_frente_e_para_tras_nao_repetem_nem_pulam_com_nomes_iguais()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = await ClienteDeAsync(api);
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await ContaAsync(api, $"igual{i}@exemplo.com.br", "Nome Igual", PerfilUsuario.Cliente));
        }

        var vistos = new List<Guid>();
        var pagina = await ListarAsync(cliente, "?perfil=Cliente&tamanho=2");
        pagina.GetProperty("anterior").ValueKind.Should().Be(JsonValueKind.Null);
        var primeira = pagina;
        JsonElement ultima;
        do
        {
            vistos.AddRange(pagina.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()));
            ultima = pagina;
            var proximo = pagina.GetProperty("proximo").GetString();
            if (proximo is null)
            {
                break;
            }

            pagina = await ListarAsync(cliente, $"?perfil=Cliente&tamanho=2&cursor={Uri.EscapeDataString(proximo)}");
        }
        while (true);

        vistos.Should().BeEquivalentTo(ids).And.OnlyHaveUniqueItems();
        ultima.GetProperty("anterior").GetString().Should().NotBeNull();

        var volta = await ListarAsync(cliente, $"?perfil=Cliente&tamanho=2&cursor={Uri.EscapeDataString(ultima.GetProperty("anterior").GetString()!)}");
        volta.GetProperty("itens").GetArrayLength().Should().Be(2);
        volta.GetProperty("proximo").GetString().Should().NotBeNull();
        primeira.GetProperty("tamanho").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Cursor_de_outros_filtros_e_400()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = await ClienteDeAsync(api);
        await ContaAsync(api, "a@exemplo.com.br", "Ana", PerfilUsuario.Cliente);
        await ContaAsync(api, "b@exemplo.com.br", "Bia", PerfilUsuario.Cliente);
        var cursor = (await ListarAsync(cliente, "?perfil=Cliente&tamanho=1")).GetProperty("proximo").GetString()!;

        var resposta = await cliente.GetAsync($"{Rota}?perfil=Admin&tamanho=1&cursor={Uri.EscapeDataString(cursor)}", Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CorpoAsync(resposta)).GetProperty("erros").TryGetProperty("cursor", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("?perfil=Auditor", "perfil")]
    [InlineData("?situacao=Ativo", "situacao")]
    [InlineData("?busca=a", "busca")]
    [InlineData("?tamanho=0", "tamanho")]
    public async Task Parametro_invalido_e_400_no_campo(string consulta, string campo)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = await ClienteDeAsync(api);

        var resposta = await cliente.GetAsync($"{Rota}{consulta}", Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CorpoAsync(resposta)).GetProperty("erros").TryGetProperty(campo, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Conta_inexistente_e_404_e_id_mal_formado_e_400()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = await ClienteDeAsync(api);

        (await cliente.GetAsync($"{Rota}/{Guid.NewGuid()}", Cancelamento)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await cliente.GetAsync($"{Rota}/nao-e-guid", Cancelamento)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Valor_da_busca_nao_aparece_em_logs_nem_em_spans()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        const string Segredo = "trechoSigiloso";
        var logs = new CapturaLogs();
        using var telemetria = new ColetorTelemetria();
        await using var api = Api(banco.NovoBanco(), logs, telemetria);
        var cliente = await ClienteDeAsync(api);

        await ListarAsync(cliente, $"?busca={Segredo}");

        logs.Textos.Should().NotContain(t => t.Contains(Segredo, StringComparison.OrdinalIgnoreCase));
        telemetria.Spans.SelectMany(s => s.TagObjects).Select(t => t.Value?.ToString())
            .Should().NotContain(v => v != null && v.Contains(Segredo, StringComparison.OrdinalIgnoreCase));
        telemetria.Spans.Select(s => s.DisplayName).Should().NotContain(n => n.Contains(Segredo, StringComparison.OrdinalIgnoreCase));
    }
}
