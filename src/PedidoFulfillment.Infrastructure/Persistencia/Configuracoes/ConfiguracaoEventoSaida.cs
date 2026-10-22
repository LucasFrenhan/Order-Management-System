using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Application.Mensageria;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

/// <summary>Outbox (REQ-06, R-04): o despachante lê os pendentes pelo índice filtrado, em ordem de Id.</summary>
internal sealed class ConfiguracaoEventoSaida : IEntityTypeConfiguration<EventoSaida>
{
    public void Configure(EntityTypeBuilder<EventoSaida> evento)
    {
        evento.ToTable("EventosSaida");
        evento.Property(e => e.Id).ValueGeneratedOnAdd();
        evento.HasIndex(e => e.IdEvento).IsUnique();
        evento.Property(e => e.Tipo).HasMaxLength(EventoSaida.TamanhoMaximoTipo);
        evento.Property(e => e.UltimoErro).HasMaxLength(EventoSaida.TamanhoMaximoErro);
        evento.HasIndex(e => new { e.ProximaTentativaEm, e.Id })
            .HasDatabaseName("IX_EventosSaida_Pendentes")
            .HasFilter("[EnviadoEm] IS NULL");
    }
}
