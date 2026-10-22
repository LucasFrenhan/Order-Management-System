using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Domain.Pagamentos;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

/// <summary>A relação com o pedido (única, em cascata) fica em <see cref="ConfiguracaoPedido"/>.</summary>
internal sealed class ConfiguracaoPagamento : IEntityTypeConfiguration<Pagamento>
{
    public void Configure(EntityTypeBuilder<Pagamento> pagamento)
    {
        pagamento.ToTable("Pagamentos", tabela =>
        {
            tabela.HasCheckConstraint("CK_Pagamentos_ValorNaoNegativo", "[Valor] >= 0");
            tabela.HasCheckConstraint("CK_Pagamentos_SituacaoValida", RestricoesEnum.Em<SituacaoPagamento>(nameof(Pagamento.Situacao)));
        });
        pagamento.Property(p => p.Id).ValueGeneratedNever();
        pagamento.Property(p => p.Valor).HasPrecision(18, 2);
        pagamento.Property(p => p.Situacao).HasConversion<string>().HasMaxLength(ContextoBancoDados.TamanhoEnum);
        pagamento.Property(p => p.IdentificadorTransacao).HasMaxLength(Pagamento.TamanhoMaximoIdentificadorTransacao);
    }
}
