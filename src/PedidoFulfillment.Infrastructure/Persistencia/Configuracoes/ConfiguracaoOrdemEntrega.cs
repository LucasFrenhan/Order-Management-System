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
        // Collation explícita, como o SKU (REQ-04, R-06): o rastreio é único sem diferenciar maiúsculas
        // em qualquer servidor (REQ-11, R-06).
        ordem.Property(o => o.Transportadora).HasMaxLength(OrdemEntrega.TamanhoMaximoTransportadora).UseCollation("SQL_Latin1_General_CP1_CI_AS");
        ordem.Property(o => o.CodigoRastreio).HasMaxLength(OrdemEntrega.TamanhoMaximoCodigoRastreio).UseCollation("SQL_Latin1_General_CP1_CI_AS");
        // Código único por transportadora; ordens ainda sem código não disputam o índice (FR-013).
        ordem.HasIndex(o => new { o.Transportadora, o.CodigoRastreio }).IsUnique().HasFilter("[CodigoRastreio] IS NOT NULL");
    }
}
