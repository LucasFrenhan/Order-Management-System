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
            // A baixa manual vem completa, e só ela preenche esses campos (REQ-28, data-model).
            tabela.HasCheckConstraint(
                "CK_Pagamentos_BaixaCompleta",
                "([Situacao] = N'EstornoBaixado' AND [EstornoBaixadoEm] IS NOT NULL AND [EstornoBaixadoPorId] IS NOT NULL AND [MotivoBaixaEstorno] IS NOT NULL)"
                + " OR ([Situacao] <> N'EstornoBaixado' AND [EstornoBaixadoEm] IS NULL AND [EstornoBaixadoPorId] IS NULL AND [MotivoBaixaEstorno] IS NULL)");
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

        // Ações da operação (REQ-28, data-model).
        pagamento.Property(p => p.UltimoErroEstorno).HasMaxLength(Pagamento.TamanhoMaximoUltimoErroEstorno);
        pagamento.Property(p => p.MotivoBaixaEstorno).HasMaxLength(Pagamento.TamanhoMaximoMotivoBaixa);
        pagamento.Property(p => p.ReferenciaExternaBaixaEstorno).HasMaxLength(Pagamento.TamanhoMaximoReferenciaExternaBaixa);

        // Primeira etapa da fila: as tentativas forçadas (R-04).
        pagamento.HasIndex(p => p.TentativaEstornoForcadaEm)
            .HasDatabaseName("IX_Pagamentos_EstornoForcado")
            .HasFilter("[Situacao] = N'EstornoPendente' AND [TentativaEstornoForcadaEm] IS NOT NULL");

        // Ordem e cursor da listagem dos pendentes (R-08).
        pagamento.HasIndex(p => new { p.EstornoSolicitadoEm, p.Id })
            .HasDatabaseName("IX_Pagamentos_EstornoPendenteListagem")
            .HasFilter("[Situacao] = N'EstornoPendente'");
    }
}
