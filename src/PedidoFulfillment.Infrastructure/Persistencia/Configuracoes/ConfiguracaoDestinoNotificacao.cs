using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Application.Notificacoes;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

/// <summary>Destinos de webhook (REQ-18, data-model): do Cliente ou globais (<c>ClienteId</c> nulo).</summary>
internal sealed class ConfiguracaoDestinoNotificacao : IEntityTypeConfiguration<DestinoNotificacao>
{
    public void Configure(EntityTypeBuilder<DestinoNotificacao> destino)
    {
        destino.ToTable("DestinosNotificacao", tabela =>
            tabela.HasCheckConstraint("CK_DestinosNotificacao_FalhasNaoNegativas", "[FalhasSeguidas] >= 0"));
        destino.Property(d => d.Id).ValueGeneratedNever();
        destino.Property(d => d.Url).HasMaxLength(DestinoNotificacao.TamanhoMaximoUrl);
        destino.Property(d => d.SegredoCifrado).HasMaxLength(DestinoNotificacao.TamanhoSegredoCifrado);
        destino.Property(d => d.MotivoDesativacao).HasMaxLength(DestinoNotificacao.TamanhoMaximoMotivo);
        destino.Property<byte[]>(ContextoBancoDados.Versao).IsRowVersion();
        destino.Ignore(d => d.Global);
        destino.HasIndex(d => d.ClienteId).HasFilter("[ClienteId] IS NOT NULL");
        destino.HasIndex(d => d.Ativo);
    }
}
