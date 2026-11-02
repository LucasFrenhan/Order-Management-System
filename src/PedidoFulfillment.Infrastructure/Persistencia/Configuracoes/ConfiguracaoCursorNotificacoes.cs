using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Infrastructure.Notificacoes;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

/// <summary>Uma linha: o último aviso distribuído em entregas pelo Worker (REQ-18, R-05 revista).</summary>
internal sealed class ConfiguracaoCursorNotificacoes : IEntityTypeConfiguration<CursorNotificacoes>
{
    public void Configure(EntityTypeBuilder<CursorNotificacoes> cursor)
    {
        cursor.ToTable("CursorNotificacoes", tabela =>
            tabela.HasCheckConstraint("CK_CursorNotificacoes_LinhaUnica", $"[Id] = {CursorNotificacoes.IdUnico}"));
        cursor.Property(c => c.Id).ValueGeneratedNever();
        cursor.Property(c => c.Posicao).HasColumnType("binary(8)");
        cursor.HasData(new CursorNotificacoes { Id = CursorNotificacoes.IdUnico, Posicao = new byte[8] });
    }
}
