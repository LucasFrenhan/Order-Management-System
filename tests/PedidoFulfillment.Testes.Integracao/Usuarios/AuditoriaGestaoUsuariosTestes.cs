using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Usuarios.CenarioGestaoUsuarios;

namespace PedidoFulfillment.Testes.Integracao.Usuarios;

/// <summary>Cada alteração efetiva gera exatamente um log, só com ações, ids e perfil (REQ-25, FR-021, SC-006, SC-007).</summary>
public sealed class AuditoriaGestaoUsuariosTestes(BancoDadosTeste banco)
{
    [Fact]
    public async Task O_ciclo_inteiro_gera_um_log_por_alteracao_com_os_ids_e_sem_dados_pessoais()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        const string Nome = "Ana Lima Sigilosa";
        const string Email = "ana.sigilosa@exemplo.com.br";
        var logs = new CapturaLogs();
        await using var api = Api(banco.NovoBanco(), logs);
        var admin = await ClienteDeAsync(api);
        var atorId = await IdDeAsync(api, EmailAdmin);

        var criada = await admin.PostAsJsonAsync(Rota, new { nome = Nome, email = Email, perfil = "OperadorLogistica" }, Cancelamento);
        var id = (await CorpoAsync(criada)).GetProperty("id").GetGuid();
        (await admin.PutAsync($"{Rota}/{id}/perfis/Admin", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.DeleteAsync($"{Rota}/{id}/perfis/Admin", Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsync($"{Rota}/{id}/desativacao", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsync($"{Rota}/{id}/reativacao", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsync($"{Rota}/{atorId}/desativacao", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        await admin.GetAsync(Rota, Cancelamento);

        var auditoria = logs.Registros.Where(r => r.Evento.Id is >= 3020 and <= 3025).ToList();

        auditoria.Select(r => r.Evento.Id).Should().Equal(3020, 3021, 3022, 3023, 3024, 3025);
        auditoria.Should().AllSatisfy(r => r.Texto.Should().Contain(atorId.ToString()));
        auditoria.Take(5).Should().AllSatisfy(r => r.Texto.Should().Contain(id.ToString()));
        auditoria.Should().AllSatisfy(r =>
        {
            r.Texto.Should().NotContain(Nome).And.NotContain(Email).And.NotContain("127.0.0.1");
            r.Texto.Should().NotContain("exemplo.com.br", "nem o e-mail do Admin");
        });
    }
}
