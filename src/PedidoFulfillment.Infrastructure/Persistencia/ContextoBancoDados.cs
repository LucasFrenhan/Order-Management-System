using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Infrastructure.Identidade;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Infrastructure.Observabilidade;

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

    /// <summary>
    /// Propriedade de sombra com o contexto W3C da criação (REQ-16, data-model), em
    /// <c>EventosSaida</c>, <c>Pedidos</c> e <c>PedidosReenvioDlq</c>.
    /// </summary>
    internal const string ContextoRastreio = "ContextoRastreio";

    private static readonly InterceptorContextoRastreio InterceptorRastreio = new();

    /// <summary>Tamanho das colunas de enum gravadas como texto (R-06).</summary>
    internal const int TamanhoEnum = 30;

    /// <summary>Chave primária de <c>ChavesIdempotencia</c> (research R-04 do REQ-05).</summary>
    public const string NomeChavePrimariaIdempotencia = "PK_ChavesIdempotencia";

    /// <summary>Sequência que gera o sufixo do número do pedido (research R-05 do REQ-05).</summary>
    internal const string SequenciaNumeroPedido = nameof(SequenciaNumeroPedido);

    public DbSet<Produto> Produtos => Set<Produto>();

    public DbSet<Pedido> Pedidos => Set<Pedido>();

    public DbSet<ItemPedido> ItensPedido => Set<ItemPedido>();

    public DbSet<HistoricoStatusPedido> HistoricosStatusPedido => Set<HistoricoStatusPedido>();

    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();

    public DbSet<OrdemEntrega> OrdensEntrega => Set<OrdemEntrega>();

    public DbSet<RegistroIdempotencia> ChavesIdempotencia => Set<RegistroIdempotencia>();

    public DbSet<EventoSaida> EventosSaida => Set<EventoSaida>();

    internal DbSet<EventoProcessado> EventosProcessados => Set<EventoProcessado>();

    public DbSet<PedidoReenvioDlq> PedidosReenvioDlq => Set<PedidoReenvioDlq>();

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

    /// <summary>
    /// Contexto de rastreamento nas linhas novas do outbox, dos pedidos e dos reenvios (REQ-16,
    /// R-03), em qualquer instância do contexto, inclusive as criadas fora da injeção.
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.AddInterceptors(InterceptorRastreio);

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
        builder.HasSequence<long>(SequenciaNumeroPedido).StartsAt(1);
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
