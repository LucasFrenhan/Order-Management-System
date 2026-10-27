using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Infrastructure.Identidade;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

internal sealed class ConfiguracaoPedido : IEntityTypeConfiguration<Pedido>
{
    public void Configure(EntityTypeBuilder<Pedido> pedido)
    {
        pedido.ToTable("Pedidos", tabela =>
        {
            tabela.HasCheckConstraint("CK_Pedidos_ValorTotalNaoNegativo", "[ValorTotal] >= 0");
            tabela.HasCheckConstraint("CK_Pedidos_StatusValido", RestricoesEnum.Em<StatusPedido>(nameof(Pedido.Status)));
        });
        pedido.Property(p => p.Id).ValueGeneratedNever();
        pedido.Property(p => p.Numero).HasMaxLength(Pedido.TamanhoNumero);
        pedido.HasIndex(p => p.Numero).IsUnique();
        pedido.Property(p => p.Status).HasConversion<string>().HasMaxLength(ContextoBancoDados.TamanhoEnum);
        pedido.Property(p => p.Moeda).HasMaxLength(3);
        pedido.Property(p => p.ValorTotal).HasPrecision(18, 2);
        pedido.Property<byte[]>(ContextoBancoDados.Versao).IsRowVersion();
        // Contexto W3C da criação, para as rotinas ligarem o pedido ao trace dele (REQ-16, R-05).
        pedido.Property<string?>(ContextoBancoDados.ContextoRastreio).HasMaxLength(Observabilidade.ContextoRastreio.Tamanho).IsUnicode(false);

        // Pedidos são histórico: o dono não pode ser apagado (FR-007).
        pedido.HasOne<Usuario>().WithMany().HasForeignKey(p => p.ClienteId).OnDelete(DeleteBehavior.Restrict);
        pedido.HasIndex(p => new { p.ClienteId, p.CriadoEm });
        pedido.HasIndex(p => p.Status);

        // Apagar o pedido apaga o que pertence a ele (FR-011).
        pedido.HasMany(p => p.Itens).WithOne().HasForeignKey(i => i.PedidoId).OnDelete(DeleteBehavior.Cascade);
        pedido.Navigation(p => p.Itens).UsePropertyAccessMode(PropertyAccessMode.Field);
        pedido.HasMany(p => p.Historico).WithOne().HasForeignKey(h => h.PedidoId).OnDelete(DeleteBehavior.Cascade);
        pedido.Navigation(p => p.Historico).UsePropertyAccessMode(PropertyAccessMode.Field);
        pedido.HasOne(p => p.Pagamento).WithOne().HasForeignKey<Pagamento>(p => p.PedidoId).OnDelete(DeleteBehavior.Cascade);
        pedido.HasOne(p => p.OrdemEntrega).WithOne().HasForeignKey<OrdemEntrega>(o => o.PedidoId).OnDelete(DeleteBehavior.Cascade);
    }
}
