using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

internal sealed class ConfiguracaoProduto : IEntityTypeConfiguration<Produto>
{
    public void Configure(EntityTypeBuilder<Produto> produto)
    {
        produto.ToTable("Produtos", tabela =>
        {
            tabela.HasCheckConstraint("CK_Produtos_PrecoNaoNegativo", "[Preco] >= 0");
            tabela.HasCheckConstraint("CK_Produtos_EstoqueNaoNegativo", "[QuantidadeEstoque] >= 0");
        });
        produto.Property(p => p.Id).ValueGeneratedNever();
        // Collation explícita: o SKU é único sem diferenciar maiúsculas em qualquer servidor (R-06).
        produto.Property(p => p.Sku).HasMaxLength(Produto.TamanhoMaximoSku).UseCollation("SQL_Latin1_General_CP1_CI_AS");
        produto.HasIndex(p => p.Sku).IsUnique();
        produto.Property(p => p.Nome).HasMaxLength(Produto.TamanhoMaximoNome);
        produto.Property(p => p.Descricao).HasMaxLength(Produto.TamanhoMaximoDescricao);
        produto.Property(p => p.Preco).HasPrecision(18, 2);
        produto.Property<byte[]>(ContextoBancoDados.Versao).IsRowVersion();
    }
}
