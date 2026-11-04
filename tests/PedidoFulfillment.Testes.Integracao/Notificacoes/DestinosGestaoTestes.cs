using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Notificacoes.CenarioWebhooks;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Notificacoes;

/// <summary>
/// Gestão dos destinos e consulta das entregas (REQ-18, US4 cenários 1 a 7; FR-011, FR-026).
/// </summary>
public sealed class DestinosGestaoTestes(BancoDadosTeste banco)
{
    private const string Rota = DestinosCadastroTestes.Rota;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> RemoverAsync(HttpClient usuario, Guid id) => usuario.DeleteAsync($"{Rota}/{id}", Token);

    private static Task<HttpResponseMessage> ReativarAsync(HttpClient usuario, Guid id) => usuario.PostAsync($"{Rota}/{id}/reativacao", null, Token);

    private static Task<HttpResponseMessage> EntregasAsync(HttpClient usuario, Guid id, string consulta = "") =>
        usuario.GetAsync($"{Rota}/{id}/entregas{consulta}", Token);

    [Fact]
    public async Task Cliente_lista_os_dele_e_Admin_os_globais_de_qualquer_Admin_sem_ver_os_de_Clientes()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var (cliente, _, _) = await ClienteAsync(api);
        var (outro, _, _) = await ClienteAsync(api);
        var admin = await AdminAsync(api);
        var (doCliente, _) = await DestinoAsync(cliente, "http://127.0.0.1:9/cliente");
        var (doOutro, _) = await DestinoAsync(outro, "http://127.0.0.1:9/outro");
        var (global, _) = await DestinoAsync(admin, "http://127.0.0.1:9/global");

        var lista = await cliente.GetStringAsync(Rota, Token);
        var listaAdmin = await admin.GetStringAsync(Rota, Token);

        lista.Should().Contain(doCliente.ToString()).And.NotContain(doOutro.ToString()).And.NotContain(global.ToString());
        listaAdmin.Should().Contain(global.ToString()).And.NotContain(doCliente.ToString()).And.NotContain(doOutro.ToString());
    }

    [Fact]
    public async Task Fora_do_escopo_e_404_e_outro_Admin_gerencia_o_global()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var (outro, _, _) = await ClienteAsync(api);
        var admin = await AdminAsync(api);
        var (doCliente, _) = await DestinoAsync(cliente, "http://127.0.0.1:9/cliente");
        var (global, _) = await DestinoAsync(admin, "http://127.0.0.1:9/global");
        var (_, emailOutroAdmin, _) = await ClienteAsync(api);
        await BancoDadosTeste.ExecutarAsync(stringConexao, $"""
            DELETE FROM UsuariosPerfis WHERE UserId = (SELECT Id FROM Usuarios WHERE Email = '{emailOutroAdmin}');
            INSERT INTO UsuariosPerfis (UserId, RoleId)
            SELECT u.Id, p.Id FROM Usuarios u CROSS JOIN Perfis p WHERE u.Email = '{emailOutroAdmin}' AND p.Name = 'Admin'
            """);
        var outroAdmin = api.CreateClient().ComToken(await Autenticacao.LoginComBancoTestes.TokenAsync(api.CreateClient(), emailOutroAdmin, Senha));

        foreach (var resposta in new[]
                 {
                     await RemoverAsync(outro, doCliente), await ReativarAsync(outro, doCliente), await EntregasAsync(outro, doCliente),
                     await RemoverAsync(cliente, global), await ReativarAsync(cliente, global), await EntregasAsync(cliente, global),
                     await RemoverAsync(admin, doCliente), await RemoverAsync(cliente, Guid.CreateVersion7())
                 })
        {
            resposta.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        (await EntregasAsync(outroAdmin, global)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReativarAsync(outroAdmin, global)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RemoverAsync(outroAdmin, global)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM DestinosNotificacao WHERE Id = '{global}'")).Should().Be(0);
    }

    [Fact]
    public async Task Sem_token_401_e_OperadorLogistica_403_nas_rotas_do_destino()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var operador = await OperadorAsync(api);
        var anonimo = api.CreateClient();
        var id = Guid.CreateVersion7();

        (await RemoverAsync(anonimo, id)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReativarAsync(anonimo, id)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await EntregasAsync(anonimo, id)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RemoverAsync(operador, id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReativarAsync(operador, id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await EntregasAsync(operador, id)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Reativacao_zera_as_falhas_e_e_idempotente_e_remocao_descarta_as_pendentes()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var (destino, _) = await DestinoAsync(cliente, "http://127.0.0.1:9/cliente");
        await BancoDadosTeste.ExecutarAsync(stringConexao,
            $"UPDATE DestinosNotificacao SET Ativo = 0, MotivoDesativacao = N'410 Gone', DesativadoEm = SYSDATETIMEOFFSET(), FalhasSeguidas = 4 WHERE Id = '{destino}'");

        var reativado = await ReativarAsync(cliente, destino);
        var deNovo = await ReativarAsync(cliente, destino);

        reativado.StatusCode.Should().Be(HttpStatusCode.OK);
        var corpo = await CorpoAsync(reativado);
        (corpo.GetProperty("ativo").GetBoolean(), corpo.GetProperty("falhasSeguidas").GetInt32()).Should().Be((true, 0));
        corpo.GetProperty("motivoDesativacao").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        deNovo.StatusCode.Should().Be(HttpStatusCode.OK);

        await PedidoComAvisoAsync(cliente, operador);
        var avisoId = await BancoDadosTeste.ConsultarAsync<long>(stringConexao, "SELECT TOP 1 Id FROM AvisosPedido");
        await BancoDadosTeste.ExecutarAsync(stringConexao, $"""
            INSERT INTO EntregasNotificacao (DestinoId, AvisoId, Situacao, Tentativas, ProximaTentativaEm, CriadaEm)
            VALUES ('{destino}', {avisoId}, N'Pendente', 2, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
            """);

        (await RemoverAsync(cliente, destino)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT Situacao FROM EntregasNotificacao WHERE AvisoId = {avisoId}"))
            .Should().Be("Descartada");
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM EntregasNotificacao WHERE DestinoId IS NULL")).Should().Be(1);
    }

    [Fact]
    public async Task Entregas_da_mais_nova_para_a_mais_antiga_com_limite()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var (destino, _) = await DestinoAsync(cliente, "http://127.0.0.1:9/cliente");
        var pedido = await PedidoComAvisoAsync(cliente, operador);
        (await MudarStatusAsync(operador, pedido, "ProntoParaEnvio")).EnsureSuccessStatusCode();
        await BancoDadosTeste.ExecutarAsync(stringConexao, $"""
            INSERT INTO EntregasNotificacao (DestinoId, AvisoId, Situacao, Tentativas, ProximaTentativaEm, CriadaEm, UltimoCodigoHttp, FinalizadaEm)
            SELECT '{destino}', Id, N'Entregue', 1, SYSDATETIMEOFFSET(), CriadoEm, 204, SYSDATETIMEOFFSET() FROM AvisosPedido ORDER BY Id
            """);

        var todas = await (await EntregasAsync(cliente, destino)).Content.ReadFromJsonAsync<List<EntregaLida>>(Token);
        var uma = await (await EntregasAsync(cliente, destino, "?limite=1")).Content.ReadFromJsonAsync<List<EntregaLida>>(Token);

        todas.Should().HaveCount(2);
        todas![0].Tipo.Should().Be("pedido.status-alterado");
        uma.Should().ContainSingle().Which.IdNotificacao.Should().Be(todas[0].IdNotificacao, "a mais nova primeiro");
        (todas[0].Situacao, todas[0].UltimoCodigoHttp, todas[0].ProximaTentativaEm).Should().Be(("Entregue", 204, (DateTimeOffset?)null));
        (await EntregasAsync(cliente, destino, "?limite=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await EntregasAsync(cliente, destino, "?limite=201")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await EntregasAsync(cliente, destino, "?limite=200")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed record EntregaLida(Guid IdNotificacao, string Tipo, Guid PedidoId, string Situacao, int Tentativas, int? UltimoCodigoHttp, DateTimeOffset? ProximaTentativaEm);
}
