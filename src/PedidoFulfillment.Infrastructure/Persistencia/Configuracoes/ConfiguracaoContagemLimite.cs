using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

/// <summary>Contagens do limite de requisições (REQ-23, data-model).</summary>
internal sealed class ConfiguracaoContagemLimite : IEntityTypeConfiguration<ContagemLimite>
{
    public void Configure(EntityTypeBuilder<ContagemLimite> contagem)
    {
        contagem.ToTable("ContagensLimite");
        contagem.HasKey(c => c.Chave).HasName("PK_ContagensLimite");
        contagem.Property(c => c.Chave).HasColumnType("binary(32)").HasMaxLength(ContagemLimite.TamanhoChave).IsFixedLength();
        contagem.Property(c => c.FimJanela).HasColumnType("datetime2(3)");
        contagem.Property(c => c.BloqueioRegistrado).HasDefaultValue(false);
        // Limpeza das contagens vencidas (R-07).
        contagem.HasIndex(c => c.FimJanela).HasDatabaseName("IX_ContagensLimite_FimJanela");
    }
}
