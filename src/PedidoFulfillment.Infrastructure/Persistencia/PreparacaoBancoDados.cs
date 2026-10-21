using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Identidade;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// Prepara o banco antes de a API aceitar requisições (research R-08): migrations, perfis e
/// usuários iniciais. Roda depois da validação de opções; uma exceção aqui impede a API de subir.
/// </summary>
internal sealed class PreparacaoBancoDados(
    IServiceScopeFactory escopos,
    IOptions<OpcoesBanco> opcoes,
    IHostEnvironment ambiente) : IHostedService
{
    public const string MensagemBancoInacessivel =
        "Não foi possível acessar o banco de dados (ConnectionStrings:SqlServer) para preparar a inicialização.";

    public const string MensagemMigrationsPendentes =
        "O banco de dados tem migrations pendentes. Aplique-as com o comando documentado antes de iniciar a API.";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!opcoes.Value.PrepararNaInicializacao)
        {
            return;
        }

        await using var escopo = escopos.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
        try
        {
            await AtualizarEsquemaAsync(contexto, cancellationToken);
            await CriarPerfisAsync(escopo.ServiceProvider.GetRequiredService<RoleManager<Perfil>>(), contexto);
        }
        catch (Exception excecao) when (ErrosBancoDados.EhFalhaConexao(excecao))
        {
            // Sem a exceção interna: ela pode trazer o servidor ou o usuário (FR-018).
            throw new InvalidOperationException(MensagemBancoInacessivel);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task AtualizarEsquemaAsync(ContextoBancoDados contexto, CancellationToken cancellationToken)
    {
        // FR-017: só Development e Testes alteram o esquema; nos demais a migration é deliberada.
        if (ambiente.IsDevelopment() || ambiente.IsEnvironment("Testes"))
        {
            await contexto.Database.MigrateAsync(cancellationToken);
        }
        else if ((await contexto.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
            throw new InvalidOperationException(MensagemMigrationsPendentes);
        }
    }

    private static async Task CriarPerfisAsync(RoleManager<Perfil> gerenciador, ContextoBancoDados contexto)
    {
        foreach (var nome in Enum.GetNames<PerfilUsuario>())
        {
            if (await gerenciador.RoleExistsAsync(nome))
            {
                continue;
            }

            try
            {
                var resultado = await gerenciador.CreateAsync(new Perfil(nome));
                // Falha de validação só é aceitável se outra instância criou o perfil agora.
                if (!resultado.Succeeded && !await gerenciador.RoleExistsAsync(nome))
                {
                    throw new InvalidOperationException($"Não foi possível criar o perfil {nome}.");
                }
            }
            catch (DbUpdateException excecao) when (ErrosBancoDados.EhViolacaoUnicidade(excecao))
            {
                // Outra instância criou o mesmo perfil ao mesmo tempo (FR-011).
                contexto.ChangeTracker.Clear();
            }
        }
    }
}
