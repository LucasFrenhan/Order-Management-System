using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Infrastructure.Mensageria;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

/// <summary>Eventos processados por consumidor (REQ-06, R-08).</summary>
internal sealed class ConfiguracaoEventoProcessado : IEntityTypeConfiguration<EventoProcessado>
{
    public void Configure(EntityTypeBuilder<EventoProcessado> processado)
    {
        processado.ToTable("EventosProcessados");
        processado.HasKey(p => new { p.Consumidor, p.IdEvento }).HasName("PK_EventosProcessados");
        processado.Property(p => p.Consumidor).HasMaxLength(EventoProcessado.TamanhoMaximoConsumidor);
    }
}
