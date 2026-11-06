using System.Net;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Autenticacao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Pedidos;
using static PedidoFulfillment.Testes.Integracao.Usuarios.CenarioGestaoUsuarios;

namespace PedidoFulfillment.Testes.Integracao.Usuarios;

/// <summary>User Story 4: desativar e reativar uma conta pelas rotas de gestão (REQ-25).</summary>
public sealed class GestaoUsuariosDesativacaoTestes(BancoDadosTeste banco)
{
    private static string RotaDesativacao(Guid id) => $"{Rota}/{id}/desativacao";

    private static string RotaReativacao(Guid id) => $"{Rota}/{id}/reativacao";

    [Fact]
    public async Task Desativar_encerra_as_sessoes_e_reativar_devolve_o_login_com_a_mesma_senha_e_os_mesmos_perfis()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var admin = await ClienteDeAsync(api);
        var id = await ContaAsync(api, "ana@exemplo.com.br", perfis: PerfilUsuario.OperadorLogistica);
        var anonimo = api.CreateClient();
        var sessao = await CenarioAutenticacao.LoginAsync(anonimo, "ana@exemplo.com.br", SenhaPadrao);

        var desativacao = await admin.PostAsync(RotaDesativacao(id), null, Cancelamento);

        desativacao.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CorpoAsync(desativacao)).GetProperty("situacao").GetString().Should().Be("Desativada");
        (await CenarioAutenticacao.TentarLoginAsync(anonimo, "ana@exemplo.com.br", SenhaPadrao)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var reativacao = await admin.PostAsync(RotaReativacao(id), null, Cancelamento);

        reativacao.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CorpoAsync(reativacao)).GetProperty("situacao").GetString().Should().Be("Ativa");
        var novoLogin = await CenarioAutenticacao.LoginAsync(anonimo, "ana@exemplo.com.br", SenhaPadrao);
        novoLogin.Perfis.Should().Equal("OperadorLogistica");
        (await CenarioAutenticacao.RenovarAsync(anonimo, sessao.Renovacao)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a sessão encerrada continua encerrada");
    }

    [Fact]
    public async Task Sem_token_e_401_e_outros_perfis_sao_403_nas_duas_rotas()
    {
        await using var api = new FabricaApi();
        var id = Guid.NewGuid();
        foreach (var rota in new[] { RotaDesativacao(id), RotaReativacao(id) })
        {
            (await api.CreateClient().PostAsync(rota, null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await api.CreateClient().ComToken(PerfilUsuario.Cliente).PostAsync(rota, null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await api.CreateClient().ComToken(PerfilUsuario.OperadorLogistica).PostAsync(rota, null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task Unico_Admin_ativo_nao_se_desativa()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var admin = await ClienteDeAsync(api);
        var atorId = await IdDeAsync(api, EmailAdmin);

        await DeveRecusarAsync(await admin.PostAsync(RotaDesativacao(atorId), null, Cancelamento), HttpStatusCode.Conflict, CodigosErro.UsuarioUltimoAdmin);

        (await LerContaAsync(api, atorId)).Desativada.Should().BeFalse();
    }

    [Fact]
    public async Task Com_outro_Admin_ativo_se_desativar_recebe_propria_conta()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var admin = await ClienteDeAsync(api);
        var atorId = await IdDeAsync(api, EmailAdmin);
        await ContaAsync(api, "outro@exemplo.com.br", perfis: PerfilUsuario.Admin);

        await DeveRecusarAsync(await admin.PostAsync(RotaDesativacao(atorId), null, Cancelamento), HttpStatusCode.Conflict, CodigosErro.UsuarioPropriaConta);
    }

    [Fact]
    public async Task Admin_desativa_outro_Admin_e_o_convidado_nao_conta_como_ativo()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var admin = await ClienteDeAsync(api);
        var outro = await ContaAsync(api, "outro@exemplo.com.br", perfis: PerfilUsuario.Admin);

        (await admin.PostAsync(RotaDesativacao(outro), null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);

        // Só resta o próprio, e o Admin desativado não conta.
        var atorId = await IdDeAsync(api, EmailAdmin);
        await DeveRecusarAsync(await admin.PostAsync(RotaDesativacao(atorId), null, Cancelamento), HttpStatusCode.Conflict, CodigosErro.UsuarioUltimoAdmin);
    }

    [Fact]
    public async Task Repetir_nao_muda_nada_nem_audita()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        await using var api = Api(banco.NovoBanco(), logs);
        var admin = await ClienteDeAsync(api);
        var id = await ContaAsync(api, "ana@exemplo.com.br", perfis: PerfilUsuario.Cliente);

        (await admin.PostAsync(RotaReativacao(id), null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        logs.Registros.Should().NotContain(r => r.Evento.Id == 3024);

        (await admin.PostAsync(RotaDesativacao(id), null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsync(RotaDesativacao(id), null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3023);
    }

    [Fact]
    public async Task Conta_inexistente_e_404_e_id_mal_formado_e_400()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var admin = await ClienteDeAsync(api);

        (await admin.PostAsync(RotaDesativacao(Guid.NewGuid()), null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsync(RotaReativacao(Guid.NewGuid()), null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsync($"{Rota}/nao-e-guid/desativacao", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Pedidos_continuam_iguais_e_o_pedido_em_andamento_segue_o_fluxo()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = CenarioPedidos.Api(banco.NovoBanco());
        var (cliente, email, _) = await CenarioPedidos.ClienteAsync(api);
        var operacaoAdmin = await CenarioPedidos.AdminAsync(api);
        var operador = await CenarioPedidos.OperadorAsync(api);
        var pedido = (await CenarioPedidos.CriadoAsync(cliente, (CenarioPedidos.Cat001, 1))).GetProperty("id").GetGuid();
        var id = await IdDeAsync(api, email);
        var antes = await (await operacaoAdmin.GetAsync($"/api/v1/pedidos/{pedido}", Cancelamento)).Content.ReadAsStringAsync(Cancelamento);

        (await operacaoAdmin.PostAsync(RotaDesativacao(id), null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);

        var duranteDesativacao = await operacaoAdmin.GetAsync($"/api/v1/pedidos/{pedido}", Cancelamento);
        duranteDesativacao.StatusCode.Should().Be(HttpStatusCode.OK);
        (await duranteDesativacao.Content.ReadAsStringAsync(Cancelamento)).Should().Be(antes);

        (await operacaoAdmin.PostAsync(RotaReativacao(id), null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await (await operacaoAdmin.GetAsync($"/api/v1/pedidos/{pedido}", Cancelamento)).Content.ReadAsStringAsync(Cancelamento)).Should().Be(antes);

        (await operacaoAdmin.PostAsync(RotaDesativacao(id), null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CenarioPedidos.MudarStatusAsync(operador, pedido, "Processando")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
