using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Chaves de idempotência (research R-03 do REQ-05). A chave primária (cliente, chave) é o que
/// impede dois pedidos para o mesmo par; o nome dela identifica a corrida (R-04).
/// </summary>
internal sealed class ConfiguracaoRegistroIdempotencia : IEntityTypeConfiguration<RegistroIdempotencia>
{
    public void Configure(EntityTypeBuilder<RegistroIdempotencia> registro)
    {
        registro.ToTable("ChavesIdempotencia");
        registro.HasKey(r => new { r.ClienteId, r.Chave }).HasName(ContextoBancoDados.NomeChavePrimariaIdempotencia);
        registro.Property(r => r.Impressao).HasMaxLength(64).IsFixedLength();
        registro.HasOne<Pedido>().WithMany().HasForeignKey(r => r.PedidoId).OnDelete(DeleteBehavior.Cascade);
        // Prepara uma limpeza futura, que só pode apagar registros com mais de 24 h (FR-009).
        registro.HasIndex(r => r.CriadoEm);
    }
}
