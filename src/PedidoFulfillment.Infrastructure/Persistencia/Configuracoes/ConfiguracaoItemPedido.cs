using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

internal sealed class ConfiguracaoItemPedido : IEntityTypeConfiguration<ItemPedido>
{
    public void Configure(EntityTypeBuilder<ItemPedido> item)
    {
        item.ToTable("ItensPedido", tabela =>
        {
            tabela.HasCheckConstraint("CK_ItensPedido_QuantidadePositiva", "[Quantidade] > 0");
            tabela.HasCheckConstraint("CK_ItensPedido_PrecoNaoNegativo", "[PrecoUnitario] >= 0");
            tabela.HasCheckConstraint("CK_ItensPedido_SubtotalNaoNegativo", "[Subtotal] >= 0");
        });
        // A chave composta impede o mesmo produto duas vezes no pedido (FR-008).
        item.HasKey(i => new { i.PedidoId, i.ProdutoId });
        item.Property(i => i.NomeProduto).HasMaxLength(Produto.TamanhoMaximoNome);
        item.Property(i => i.PrecoUnitario).HasPrecision(18, 2);
        item.Property(i => i.Subtotal).HasPrecision(18, 2);
        // Produto citado em pedido não pode ser apagado (FR-004).
        item.HasOne<Produto>().WithMany().HasForeignKey(i => i.ProdutoId).OnDelete(DeleteBehavior.Restrict);
    }
}
