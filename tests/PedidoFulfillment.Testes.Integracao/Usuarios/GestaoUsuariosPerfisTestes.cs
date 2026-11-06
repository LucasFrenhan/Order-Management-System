using System.Net;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Autenticacao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Usuarios.CenarioGestaoUsuarios;

namespace PedidoFulfillment.Testes.Integracao.Usuarios;

/// <summary>User Story 3: atribuir e retirar perfis (REQ-25).</summary>
public sealed class GestaoUsuariosPerfisTestes(BancoDadosTeste banco)
{
    private static string RotaPerfil(Guid id, object perfil) => $"{Rota}/{id}/perfis/{perfil}";

    private static async Task<IEnumerable<string?>> PerfisAsync(HttpResponseMessage resposta)
    {
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await CorpoAsync(resposta)).GetProperty("perfis").EnumerateArray().Select(p => p.GetString());
    }

    [Fact]
    public async Task Atribui_e_retira_com_os_perfis_atuais_na_resposta_e_a_auditoria()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        await using var api = Api(banco.NovoBanco(), logs);
        var admin = await ClienteDeAsync(api);
        var atorId = await IdDeAsync(api, EmailAdmin);
        var id = await ContaAsync(api, "ana@exemplo.com.br", "Ana Lima", PerfilUsuario.OperadorLogistica);

        var atribuicao = await admin.PutAsync(RotaPerfil(id, PerfilUsuario.Admin), null, Cancelamento);

        (await PerfisAsync(atribuicao)).Should().BeEquivalentTo("Admin", "OperadorLogistica");
        var auditoria = logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3021).Subject;
        auditoria.Texto.Should().Contain(id.ToString()).And.Contain(atorId.ToString()).And.Contain("Admin");
        auditoria.Texto.Should().NotContain("ana@exemplo.com.br").And.NotContain("Ana Lima");

        var retirada = await admin.DeleteAsync(RotaPerfil(id, PerfilUsuario.OperadorLogistica), Cancelamento);

        (await PerfisAsync(retirada)).Should().BeEquivalentTo("Admin");
        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3022 && r.Texto.Contains("OperadorLogistica"));
    }

    [Fact]
    public async Task Sem_token_e_401_e_outros_perfis_sao_403_nas_duas_rotas()
    {
        await using var api = new FabricaApi();
        var rota = RotaPerfil(Guid.NewGuid(), PerfilUsuario.Admin);

        (await api.CreateClient().PutAsync(rota, null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await api.CreateClient().DeleteAsync(rota, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        foreach (var perfil in new[] { PerfilUsuario.Cliente, PerfilUsuario.OperadorLogistica })
        {
            (await api.CreateClient().ComToken(perfil).PutAsync(rota, null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await api.CreateClient().ComToken(perfil).DeleteAsync(rota, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Unico_Admin_ativo_nao_retira_o_proprio_perfil_e_a_recusa_e_auditada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        await using var api = Api(banco.NovoBanco(), logs);
        var admin = await ClienteDeAsync(api);
        var atorId = await IdDeAsync(api, EmailAdmin);

        await DeveRecusarAsync(await admin.DeleteAsync(RotaPerfil(atorId, PerfilUsuario.Admin), Cancelamento), HttpStatusCode.Conflict, CodigosErro.UsuarioUltimoAdmin);

        (await LerContaAsync(api, atorId)).Perfis.Should().Contain(PerfilUsuario.Admin);
        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3025 && r.Nivel == Microsoft.Extensions.Logging.LogLevel.Warning && r.Texto.Contains(CodigosErro.UsuarioUltimoAdmin));
    }

    [Fact]
    public async Task Com_outro_Admin_ativo_retirar_o_proprio_Admin_recebe_propria_conta()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        await using var api = Api(banco.NovoBanco(), logs);
        var admin = await ClienteDeAsync(api);
        var atorId = await IdDeAsync(api, EmailAdmin);
        await ContaAsync(api, "outro@exemplo.com.br", perfis: PerfilUsuario.Admin);

        await DeveRecusarAsync(await admin.DeleteAsync(RotaPerfil(atorId, PerfilUsuario.Admin), Cancelamento), HttpStatusCode.Conflict, CodigosErro.UsuarioPropriaConta);

        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3025 && r.Texto.Contains(CodigosErro.UsuarioPropriaConta));
    }

    [Fact]
    public async Task Rebaixar_Admin_de_perfil_unico_recebe_ultimo_perfil_e_atribuir_operador_antes_resolve()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var admin = await ClienteDeAsync(api);
        var outro = await ContaAsync(api, "outro@exemplo.com.br", perfis: PerfilUsuario.Admin);

        await DeveRecusarAsync(await admin.DeleteAsync(RotaPerfil(outro, PerfilUsuario.Admin), Cancelamento), HttpStatusCode.Conflict, CodigosErro.UsuarioUltimoPerfil);

        (await admin.PutAsync(RotaPerfil(outro, PerfilUsuario.OperadorLogistica), null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PerfisAsync(await admin.DeleteAsync(RotaPerfil(outro, PerfilUsuario.Admin), Cancelamento))).Should().Equal("OperadorLogistica");
    }

    [Fact]
    public async Task Retirar_o_perfil_de_um_Cliente_recebe_ultimo_perfil()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var admin = await ClienteDeAsync(api);
        var cliente = await ContaAsync(api, "cliente@exemplo.com.br", perfis: PerfilUsuario.Cliente);

        await DeveRecusarAsync(await admin.DeleteAsync(RotaPerfil(cliente, PerfilUsuario.Cliente), Cancelamento), HttpStatusCode.Conflict, CodigosErro.UsuarioUltimoPerfil);
    }

    [Theory]
    [InlineData(PerfilUsuario.Cliente, PerfilUsuario.Admin)]
    [InlineData(PerfilUsuario.Cliente, PerfilUsuario.OperadorLogistica)]
    [InlineData(PerfilUsuario.OperadorLogistica, PerfilUsuario.Cliente)]
    public async Task Atribuicao_que_mistura_grupos_recebe_perfis_incompativeis(PerfilUsuario atual, PerfilUsuario pedido)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var admin = await ClienteDeAsync(api);
        var id = await ContaAsync(api, "conta@exemplo.com.br", perfis: atual);

        await DeveRecusarAsync(await admin.PutAsync(RotaPerfil(id, pedido), null, Cancelamento), HttpStatusCode.Conflict, CodigosErro.UsuarioPerfisIncompativeis);

        (await LerContaAsync(api, id)).Perfis.Should().Equal(atual);
    }

    [Fact]
    public async Task Repetir_nao_muda_nada_nem_audita()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        await using var api = Api(banco.NovoBanco(), logs);
        var admin = await ClienteDeAsync(api);
        var id = await ContaAsync(api, "ana@exemplo.com.br", perfis: PerfilUsuario.OperadorLogistica);

        (await PerfisAsync(await admin.PutAsync(RotaPerfil(id, PerfilUsuario.OperadorLogistica), null, Cancelamento))).Should().Equal("OperadorLogistica");
        (await PerfisAsync(await admin.DeleteAsync(RotaPerfil(id, PerfilUsuario.Admin), Cancelamento))).Should().Equal("OperadorLogistica");

        logs.Registros.Should().NotContain(r => r.Evento.Id == 3021 || r.Evento.Id == 3022);
    }

    [Fact]
    public async Task Conta_inexistente_e_404_e_perfil_desconhecido_ou_id_mal_formado_sao_400()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var admin = await ClienteDeAsync(api);
        var id = await ContaAsync(api, "ana@exemplo.com.br", perfis: PerfilUsuario.OperadorLogistica);

        (await admin.PutAsync(RotaPerfil(Guid.NewGuid(), PerfilUsuario.Admin), null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.DeleteAsync(RotaPerfil(Guid.NewGuid(), PerfilUsuario.Admin), Cancelamento)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        foreach (var desconhecido in new[] { "Auditor", "admin", "1" })
        {
            var resposta = await admin.PutAsync(RotaPerfil(id, desconhecido), null, Cancelamento);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await CorpoAsync(resposta)).GetProperty("erros").TryGetProperty("perfil", out _).Should().BeTrue();
        }

        (await admin.PutAsync($"{Rota}/nao-e-guid/perfis/Admin", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_renovacao_traz_os_perfis_novos_e_o_token_antigo_mantem_os_antigos_ate_expirar()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var admin = await ClienteDeAsync(api);
        var id = await ContaAsync(api, "ana@exemplo.com.br", perfis: [PerfilUsuario.Admin, PerfilUsuario.OperadorLogistica]);
        var anonimo = api.CreateClient();
        var login = await CenarioAutenticacao.LoginAsync(anonimo, "ana@exemplo.com.br", SenhaPadrao);
        login.Perfis.Should().BeEquivalentTo("Admin", "OperadorLogistica");

        (await admin.DeleteAsync(RotaPerfil(id, PerfilUsuario.Admin), Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Fora da gestão, o token antigo segue valendo (FR-002): a rota da DLQ só olha o perfil no token.
        (await api.CreateClient().ComToken(login.Acesso).GetAsync($"/api/v1/admin/dlq/reenvios/{Guid.NewGuid()}", Cancelamento))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        var renovado = await CenarioAutenticacao.TokensAsync(await CenarioAutenticacao.RenovarAsync(anonimo, login.Renovacao));
        renovado.Perfis.Should().Equal("OperadorLogistica");
        (await api.CreateClient().ComToken(renovado.Acesso).GetAsync($"/api/v1/admin/dlq/reenvios/{Guid.NewGuid()}", Cancelamento))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
