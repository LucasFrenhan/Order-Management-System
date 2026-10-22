using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Domain.Entregas;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

/// <summary>A relação com o pedido (única, em cascata) fica em <see cref="ConfiguracaoPedido"/>.</summary>
internal sealed class ConfiguracaoOrdemEntrega : IEntityTypeConfiguration<OrdemEntrega>
{
    public void Configure(EntityTypeBuilder<OrdemEntrega> ordem)
    {
        ordem.ToTable("OrdensEntrega");
        ordem.Property(o => o.Id).ValueGeneratedNever();
        ordem.Property(o => o.Transportadora).HasMaxLength(OrdemEntrega.TamanhoMaximoTransportadora);
        ordem.Property(o => o.CodigoRastreio).HasMaxLength(OrdemEntrega.TamanhoMaximoCodigoRastreio);
        // Código único por transportadora; ordens ainda sem código não disputam o índice (FR-013).
        ordem.HasIndex(o => new { o.Transportadora, o.CodigoRastreio }).IsUnique().HasFilter("[CodigoRastreio] IS NOT NULL");
    }
}
