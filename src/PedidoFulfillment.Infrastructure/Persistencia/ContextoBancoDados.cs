using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Infrastructure.Identidade;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// Banco da aplicação. Nasce com as tabelas do Identity (REQ-03); o REQ-04 acrescenta aqui os
/// <c>DbSet</c> de negócio, em migrations seguintes.
/// </summary>
public sealed class ContextoBancoDados(DbContextOptions<ContextoBancoDados> options)
    : IdentityDbContext<Usuario, Perfil, Guid>(options)
{
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
    }
}
