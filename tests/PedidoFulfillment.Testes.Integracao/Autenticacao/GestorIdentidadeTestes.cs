using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Infrastructure.Identidade;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>
/// O gestor de identidade chamado direto, para o que a API não deixa chegar até ele: uma recusa
/// do Identity que não é e-mail repetido e um perfil no banco que o sistema não conhece (REQ-02).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class GestorIdentidadeTestes(BancoDadosTeste banco)
{
    private const string Senha = "SenhaForte1";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private FabricaApi Api() => new() { PrepararBanco = true, StringConexao = banco.NovoBanco() };

    [Fact]
    public async Task Recusa_do_Identity_que_nao_e_email_repetido_e_erro_inesperado_sem_o_email()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api();
        await using var escopo = api.Services.CreateAsyncScope();
        var gestor = escopo.ServiceProvider.GetRequiredService<IGestorIdentidade>();

        var criar = () => gestor.CriarAsync("Maria", "maria@exemplo.com.br", "curta", Cancelamento);

        (await criar.Should().ThrowAsync<InvalidOperationException>()).Which.Message
            .Should().Contain("PasswordTooShort").And.NotContain("maria@exemplo.com.br");
    }

    [Fact]
    public async Task Perfil_desconhecido_no_banco_fica_fora_das_credenciais()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api();
        await using var escopo = api.Services.CreateAsyncScope();
        var gestor = escopo.ServiceProvider.GetRequiredService<IGestorIdentidade>();
        var criado = await gestor.CriarAsync("Maria", "maria@exemplo.com.br", Senha, Cancelamento);
        var papeis = escopo.ServiceProvider.GetRequiredService<RoleManager<Perfil>>();
        var usuarios = escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        (await papeis.CreateAsync(new Perfil { Name = "Auditor" })).Succeeded.Should().BeTrue();
        (await usuarios.AddToRoleAsync((await usuarios.FindByIdAsync(criado.Valor!.Id.ToString()))!, "Auditor")).Succeeded.Should().BeTrue();

        var credenciais = await gestor.VerificarCredenciaisAsync("maria@exemplo.com.br", Senha, Cancelamento);

        credenciais.Perfis.Should().Equal(PerfilUsuario.Cliente);
    }
}
