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

        // Busca do catálogo (REQ-22, research R-03 e R-05): coluna do índice full-text, criado na
        // migration CatalogoProdutos, e os índices da listagem por nome e SKU.
        produto.Property<string>(InterceptorTextoBusca.Propriedade).HasMaxLength(InterceptorTextoBusca.Tamanho).IsRequired();
        produto.HasIndex(p => new { p.Nome, p.Sku })
            .IncludeProperties(p => new { p.Preco, p.QuantidadeEstoque, p.Ativo })
            .HasDatabaseName("IX_Produtos_Nome_Sku");
        produto.HasIndex(p => new { p.Ativo, p.Nome, p.Sku })
            .IncludeProperties(p => new { p.Preco, p.QuantidadeEstoque })
            .HasDatabaseName("IX_Produtos_Ativo_Nome_Sku");
    }
}
