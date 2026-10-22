using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Infrastructure.Identidade;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// Banco da aplicação. O REQ-03 criou as tabelas do Identity; o REQ-04 acrescentou as entidades
/// de negócio, mapeadas em <c>Persistencia/Configuracoes</c>.
/// </summary>
public sealed class ContextoBancoDados(DbContextOptions<ContextoBancoDados> options)
    : IdentityDbContext<Usuario, Perfil, Guid>(options)
{
    /// <summary>Propriedade de sombra <c>rowversion</c> de Produto e Pedido (research R-04).</summary>
    internal const string Versao = "Versao";

    /// <summary>Tamanho das colunas de enum gravadas como texto (R-06).</summary>
    internal const int TamanhoEnum = 30;

    public DbSet<Produto> Produtos => Set<Produto>();

    public DbSet<Pedido> Pedidos => Set<Pedido>();

    public DbSet<ItemPedido> ItensPedido => Set<ItemPedido>();

    public DbSet<HistoricoStatusPedido> HistoricosStatusPedido => Set<HistoricoStatusPedido>();

    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();

    public DbSet<OrdemEntrega> OrdensEntrega => Set<OrdemEntrega>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        catch (DbUpdateConcurrencyException excecao) when (EhEntidadeNegocio(excecao))
        {
            throw Conflito(excecao);
        }
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        catch (DbUpdateConcurrencyException excecao) when (EhEntidadeNegocio(excecao))
        {
            throw Conflito(excecao);
        }
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Usuario>(usuario =>
        {
            usuario.ToTable("Usuarios");
            usuario.Property(u => u.Nome).HasMaxLength(100).IsRequired();
            // Único: o segundo de dois registros simultâneos esbarra aqui (research R-03).
            usuario.HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
        });
        builder.Entity<Perfil>().ToTable("Perfis");
        builder.Entity<IdentityUserRole<Guid>>().ToTable("UsuariosPerfis");
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("UsuariosClaims");
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("PerfisClaims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("UsuariosLogins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("UsuariosTokens");

        builder.ApplyConfigurationsFromAssembly(typeof(ContextoBancoDados).Assembly);
    }

    /// <summary>
    /// Só entidades do Domain viram <see cref="ConflitoConcorrenciaException"/>. As do Identity
    /// seguem com a exceção original, que o <c>UserStore</c> converte em
    /// <c>IdentityResult.Failed(ConcurrencyFailure)</c> (research R-04, FR-019 do REQ-04).
    /// </summary>
    private static bool EhEntidadeNegocio(DbUpdateConcurrencyException excecao) =>
        excecao.Entries.Count > 0 && excecao.Entries[0].Metadata.ClrType.Assembly == typeof(Pedido).Assembly;

    private static ConflitoConcorrenciaException Conflito(DbUpdateConcurrencyException excecao)
    {
        var entrada = excecao.Entries[0];
        var chave = entrada.Metadata.FindPrimaryKey()!.Properties.Select(p => entrada.Property(p.Name).CurrentValue);
        return new ConflitoConcorrenciaException(entrada.Metadata.ClrType.Name, string.Join('/', chave), excecao);
    }
}
