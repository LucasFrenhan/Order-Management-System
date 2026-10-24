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
            tabela.HasCheckConstraint("CK_Pagamentos_TentativasEstornoNaoNegativas", "[TentativasEstorno] >= 0");
        });
        pagamento.Property(p => p.Id).ValueGeneratedNever();
        pagamento.Property(p => p.Valor).HasPrecision(18, 2);
        pagamento.Property(p => p.Situacao).HasConversion<string>().HasMaxLength(ContextoBancoDados.TamanhoEnum);
        pagamento.Property(p => p.IdentificadorTransacao).HasMaxLength(Pagamento.TamanhoMaximoIdentificadorTransacao);
        pagamento.Property(p => p.IdentificadorEstorno).HasMaxLength(Pagamento.TamanhoMaximoIdentificadorEstorno);

        // Fila da rodada de estornos (REQ-13, R-04): os que nunca falharam primeiro (nulos vêm antes
        // no SQL Server), depois o tentado há mais tempo.
        pagamento.HasIndex(p => new { p.UltimaFalhaEstornoEm, p.EstornoSolicitadoEm })
            .HasDatabaseName("IX_Pagamentos_EstornoPendente")
            .HasFilter("[Situacao] = N'EstornoPendente'");
    }
}
