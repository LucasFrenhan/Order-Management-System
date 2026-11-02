using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Application.Notificacoes;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Avisos de notificação (REQ-18, R-13): lidos por <c>Posicao</c> abaixo de
/// <c>MIN_ACTIVE_ROWVERSION()</c>, inteiros ou por Cliente ou pedido (reenvio do SSE).
/// </summary>
internal sealed class ConfiguracaoAvisoPedido : IEntityTypeConfiguration<AvisoPedido>
{
    /// <summary>Coluna <c>rowversion</c> de sombra, o cursor da leitura.</summary>
    internal const string Posicao = "Posicao";

    public void Configure(EntityTypeBuilder<AvisoPedido> aviso)
    {
        aviso.ToTable("AvisosPedido");
        aviso.Property(a => a.Id).ValueGeneratedOnAdd();
        aviso.Property<byte[]>(Posicao).IsRowVersion();
        aviso.Property(a => a.Tipo).HasMaxLength(AvisoPedido.TamanhoMaximoTipo).IsUnicode(false);
        // Contexto W3C de quem gravou a mudança, ligado ao envio do webhook (REQ-16, R-03).
        aviso.Property<string?>(ContextoBancoDados.ContextoRastreio).HasMaxLength(Observabilidade.ContextoRastreio.Tamanho).IsUnicode(false);
        aviso.HasIndex(Posicao).IsUnique().HasDatabaseName("IX_AvisosPedido_Posicao");
        aviso.HasIndex(a => a.IdNotificacao).IsUnique();
        aviso.HasIndex(nameof(AvisoPedido.ClienteId), Posicao).HasDatabaseName("IX_AvisosPedido_ClienteId_Posicao");
        aviso.HasIndex(nameof(AvisoPedido.PedidoId), Posicao).HasDatabaseName("IX_AvisosPedido_PedidoId_Posicao");
        aviso.HasIndex(a => a.CriadoEm);
        aviso.HasOne<Pedido>().WithMany().HasForeignKey(a => a.PedidoId).OnDelete(DeleteBehavior.NoAction);
    }
}
