using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Infrastructure.Identidade;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

internal sealed class ConfiguracaoHistoricoStatusPedido : IEntityTypeConfiguration<HistoricoStatusPedido>
{
    public void Configure(EntityTypeBuilder<HistoricoStatusPedido> historico)
    {
        var statusAnterior = RestricoesEnum.Em<StatusPedido>(nameof(HistoricoStatusPedido.StatusAnterior));
        var statusNovo = RestricoesEnum.Em<StatusPedido>(nameof(HistoricoStatusPedido.StatusNovo));
        historico.ToTable("HistoricosStatusPedido", tabela => tabela.HasCheckConstraint(
            "CK_HistoricosStatusPedido_StatusValido",
            $"([StatusAnterior] IS NULL OR {statusAnterior}) AND {statusNovo}"));
        // Identity: a ordem de inclusão é a ordem do histórico (R-06).
        historico.Property(h => h.Id).ValueGeneratedOnAdd();
        historico.Property(h => h.StatusAnterior).HasConversion<string>().HasMaxLength(ContextoBancoDados.TamanhoEnum);
        historico.Property(h => h.StatusNovo).HasConversion<string>().HasMaxLength(ContextoBancoDados.TamanhoEnum);
        historico.Property(h => h.Motivo).HasMaxLength(HistoricoStatusPedido.TamanhoMaximoMotivo);
        historico.HasIndex(h => new { h.PedidoId, h.Id });
        historico.HasOne<Usuario>().WithMany().HasForeignKey(h => h.AutorId).OnDelete(DeleteBehavior.Restrict);
    }
}
