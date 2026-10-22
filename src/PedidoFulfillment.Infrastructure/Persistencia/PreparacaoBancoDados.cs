using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Identidade;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// Prepara o banco antes de a API aceitar requisições (research R-08): migrations, perfis,
/// usuários iniciais e, só em Development e Testes, o catálogo inicial (REQ-04, FR-020). Roda
/// depois da validação de opções; uma exceção aqui impede a API de subir.
/// </summary>
internal sealed partial class PreparacaoBancoDados(
    IServiceScopeFactory escopos,
    IOptions<OpcoesBanco> opcoes,
    IOptions<OpcoesUsuariosIniciais> usuariosIniciais,
    IHostEnvironment ambiente,
    TimeProvider relogio,
    ILogger<PreparacaoBancoDados> logger) : IHostedService
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
            await CriarUsuariosIniciaisAsync(escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>(), contexto, cancellationToken);
            if (PodeAlterarDados())
            {
                await CriarCatalogoInicialAsync(contexto, cancellationToken);
            }
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
        if (PodeAlterarDados())
        {
            await contexto.Database.MigrateAsync(cancellationToken);
        }
        else if ((await contexto.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
            throw new InvalidOperationException(MensagemMigrationsPendentes);
        }
    }

    private bool PodeAlterarDados() => ambiente.IsDevelopment() || ambiente.IsEnvironment("Testes");

    /// <summary>
    /// FR-020: insere só os SKUs ausentes; um produto existente nunca é alterado.
    /// </summary>
    private async Task CriarCatalogoInicialAsync(ContextoBancoDados contexto, CancellationToken cancellationToken)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            var existentes = await contexto.Produtos.Select(p => p.Sku).ToListAsync(cancellationToken);
            var faltantes = CatalogoInicial.Produtos(relogio.GetUtcNow())
                .Where(p => !existentes.Contains(p.Sku, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (faltantes.Count == 0)
            {
                return;
            }

            contexto.Produtos.AddRange(faltantes);
            try
            {
                await contexto.SaveChangesAsync(cancellationToken);
                CatalogoInicialCriado(logger, faltantes.Count);
                return;
            }
            catch (DbUpdateException excecao) when (tentativa == 1 && ErrosBancoDados.EhViolacaoUnicidade(excecao))
            {
                // Outra instância inseriu ao mesmo tempo: relê e tenta uma vez só com o que falta.
                contexto.ChangeTracker.Clear();
            }
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

    /// <summary>
    /// FR-012: cria só as contas que não existem; uma conta existente nunca é alterada, nem a
    /// senha nem os perfis.
    /// </summary>
    private async Task CriarUsuariosIniciaisAsync(UserManager<Usuario> usuarios, ContextoBancoDados contexto, CancellationToken cancellationToken)
    {
        foreach (var inicial in usuariosIniciais.Value.Usuarios)
        {
            var email = inicial.Email.Trim();
            if (await usuarios.FindByEmailAsync(email) is not null)
            {
                UsuarioInicialExistente(logger, MascaraEmail.Mascarar(email));
                continue;
            }

            var usuario = new Usuario { UserName = email, Email = email, Nome = inicial.Nome.Trim() };
            await using var transacao = await contexto.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                Garantir(await usuarios.CreateAsync(usuario, inicial.Senha));
                Garantir(await usuarios.AddToRoleAsync(usuario, inicial.Perfil));
                await transacao.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException excecao) when (ErrosBancoDados.EhViolacaoUnicidade(excecao))
            {
                // Outra instância criou a mesma conta ao mesmo tempo.
                contexto.ChangeTracker.Clear();
            }
        }
    }

    // Só os códigos do Identity: as descrições podem repetir o e-mail configurado.
    private static void Garantir(IdentityResult resultado)
    {
        if (!resultado.Succeeded)
        {
            throw new InvalidOperationException(
                $"Não foi possível criar um usuário inicial: {string.Join(", ", resultado.Errors.Select(e => e.Code))}.");
        }
    }

    [LoggerMessage(EventId = 3005, EventName = "UsuarioInicialExistente", Level = LogLevel.Warning,
        Message = "Usuário inicial {EmailMascarado} já existe e não foi alterado.")]
    private static partial void UsuarioInicialExistente(ILogger logger, string emailMascarado);

    [LoggerMessage(EventId = 3006, EventName = "CatalogoInicialCriado", Level = LogLevel.Information,
        Message = "Catálogo inicial: {Quantidade} produto(s) criado(s).")]
    private static partial void CatalogoInicialCriado(ILogger logger, int quantidade);
}
