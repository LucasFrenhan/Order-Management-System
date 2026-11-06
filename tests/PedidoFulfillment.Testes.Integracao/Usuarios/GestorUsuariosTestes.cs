using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Usuarios;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Infrastructure.Identidade;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Usuarios.CenarioGestaoUsuarios;

namespace PedidoFulfillment.Testes.Integracao.Usuarios;

/// <summary>O gestor chamado direto: quem conta como Admin ativo e a serialização das alterações (REQ-25, R-03).</summary>
public sealed class GestorUsuariosTestes(BancoDadosTeste banco)
{
    private static async Task<bool> EhAdminAtivoAsync(FabricaApi api, Guid id)
    {
        await using var escopo = api.Services.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<IGestorUsuarios>().EhAdminAtivoAsync(id, Cancelamento);
    }

    [Fact]
    public async Task Conta_como_Admin_ativo_o_bloqueado_e_nao_conta_o_desativado_o_sem_senha_nem_outro_perfil()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var atorId = await IdDeAsync(api, EmailAdmin);
        var bloqueado = await ContaAsync(api, "bloqueado@exemplo.com.br", perfis: PerfilUsuario.Admin);
        var desativado = await ContaAsync(api, "desativado@exemplo.com.br", perfis: PerfilUsuario.Admin);
        var operador = await ContaAsync(api, "operador@exemplo.com.br", perfis: PerfilUsuario.OperadorLogistica);
        Guid semSenha;
        await using (var escopo = api.Services.CreateAsyncScope())
        {
            var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
            var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
            var conta = new Usuario { UserName = "semsenha@exemplo.com.br", Email = "semsenha@exemplo.com.br", Nome = "Sem Senha" };
            (await usuarios.CreateAsync(conta)).Succeeded.Should().BeTrue();
            (await usuarios.AddToRoleAsync(conta, nameof(PerfilUsuario.Admin))).Succeeded.Should().BeTrue();
            semSenha = conta.Id;
            await contexto.Users.Where(u => u.Id == bloqueado)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.LockoutEnd, DateTimeOffset.UtcNow.AddHours(1)), Cancelamento);
            await contexto.Users.Where(u => u.Id == desativado)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DesativadoEm, DateTimeOffset.UtcNow), Cancelamento);
        }

        (await EhAdminAtivoAsync(api, atorId)).Should().BeTrue();
        (await EhAdminAtivoAsync(api, bloqueado)).Should().BeTrue("o bloqueio é temporário");
        (await EhAdminAtivoAsync(api, desativado)).Should().BeFalse();
        (await EhAdminAtivoAsync(api, semSenha)).Should().BeFalse("ainda não consegue entrar");
        (await EhAdminAtivoAsync(api, operador)).Should().BeFalse();
        (await EhAdminAtivoAsync(api, Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task Alteracao_espera_o_lock_e_sem_ele_falha_sem_mudar_nada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var atorId = await IdDeAsync(api, EmailAdmin);
        var alvo = await ContaAsync(api, "cliente@exemplo.com.br", perfis: PerfilUsuario.Cliente);

        // Outra transação segura o lock durante a chamada.
        await using var escopoLock = api.Services.CreateAsyncScope();
        var contextoLock = escopoLock.ServiceProvider.GetRequiredService<ContextoBancoDados>();
        await using var transacaoLock = await contextoLock.Database.BeginTransactionAsync(Cancelamento);
        await contextoLock.Database.ExecuteSqlAsync(
            $"EXEC sp_getapplock @Resource = {GestorUsuarios.RecursoLock}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 5000;", Cancelamento);

        await using var escopo = api.Services.CreateAsyncScope();
        var gestor = escopo.ServiceProvider.GetRequiredService<IGestorUsuarios>();
        var inicio = TimeProvider.System.GetTimestamp();
        var alterar = () => gestor.AlterarAsync(atorId, alvo, new AlteracaoUsuario(TipoAlteracaoUsuario.Desativar), Cancelamento);

        await alterar.Should().ThrowAsync<InvalidOperationException>();

        TimeProvider.System.GetElapsedTime(inicio).Should().BeGreaterThan(TimeSpan.FromSeconds(GestorUsuarios.EsperaLockMilissegundos / 1000 - 1));
        await transacaoLock.RollbackAsync(Cancelamento);
        (await LerContaAsync(api, alvo)).Desativada.Should().BeFalse();
    }

    [Fact]
    public async Task Excecao_no_meio_da_alteracao_nao_deixa_nada_gravado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var atorId = await IdDeAsync(api, EmailAdmin);
        var alvo = await ContaAsync(api, "cliente@exemplo.com.br", perfis: PerfilUsuario.Cliente);
        await using var escopo = api.Services.CreateAsyncScope();
        var gestor = escopo.ServiceProvider.GetRequiredService<IGestorUsuarios>();
        using var cancelado = new CancellationTokenSource();
        await cancelado.CancelAsync();

        var alterar = () => gestor.AlterarAsync(atorId, alvo, new AlteracaoUsuario(TipoAlteracaoUsuario.Desativar), cancelado.Token);

        await alterar.Should().ThrowAsync<OperationCanceledException>();
        (await LerContaAsync(api, alvo)).Desativada.Should().BeFalse();
    }
}
