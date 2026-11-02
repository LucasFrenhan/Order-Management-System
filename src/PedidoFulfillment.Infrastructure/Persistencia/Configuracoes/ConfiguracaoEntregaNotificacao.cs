using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Application.Notificacoes;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Entregas de webhook (REQ-18, data-model): o índice único impede entrega repetida na
/// distribuição; o filtrado das pendentes serve ao envio; o de <c>FinalizadaEm</c>, à limpeza.
/// </summary>
internal sealed class ConfiguracaoEntregaNotificacao : IEntityTypeConfiguration<EntregaNotificacao>
{
    public void Configure(EntityTypeBuilder<EntregaNotificacao> entrega)
    {
        entrega.ToTable("EntregasNotificacao", tabela =>
        {
            tabela.HasCheckConstraint("CK_EntregasNotificacao_SituacaoValida", RestricoesEnum.Em<SituacaoEntrega>(nameof(EntregaNotificacao.Situacao)));
            tabela.HasCheckConstraint("CK_EntregasNotificacao_TentativasNaoNegativas", "[Tentativas] >= 0");
        });
        entrega.Property(e => e.Id).ValueGeneratedOnAdd();
        entrega.Property(e => e.Situacao).HasConversion<string>().HasMaxLength(ContextoBancoDados.TamanhoEnum);
        entrega.Property(e => e.UltimoErro).HasMaxLength(EntregaNotificacao.TamanhoMaximoErro);
        entrega.Property(e => e.ContextoRastreio).HasMaxLength(Observabilidade.ContextoRastreio.Tamanho).IsUnicode(false);
        entrega.HasIndex(e => new { e.DestinoId, e.AvisoId }).IsUnique();
        entrega.HasIndex(e => new { e.Situacao, e.ProximaTentativaEm })
            .HasDatabaseName("IX_EntregasNotificacao_Pendentes")
            .HasFilter("[Situacao] = N'Pendente'");
        entrega.HasIndex(e => e.FinalizadaEm)
            .HasDatabaseName("IX_EntregasNotificacao_Finalizadas")
            .HasFilter("[FinalizadaEm] IS NOT NULL");
        entrega.HasOne<DestinoNotificacao>().WithMany().HasForeignKey(e => e.DestinoId).OnDelete(DeleteBehavior.SetNull);
        entrega.HasOne<AvisoPedido>().WithMany().HasForeignKey(e => e.AvisoId).OnDelete(DeleteBehavior.NoAction);
    }
}
