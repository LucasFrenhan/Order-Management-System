using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Application.Mensageria;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

/// <summary>Pedidos de reenvio da DLQ (REQ-14, R-07): a fila do executor e a limpeza usam (Situacao, RegistradoEm).</summary>
internal sealed class ConfiguracaoPedidoReenvioDlq : IEntityTypeConfiguration<PedidoReenvioDlq>
{
    public void Configure(EntityTypeBuilder<PedidoReenvioDlq> pedido)
    {
        pedido.ToTable("PedidosReenvioDlq", tabela =>
        {
            tabela.HasCheckConstraint("CK_PedidosReenvioDlq_SituacaoValida", RestricoesEnum.Em<SituacaoReenvioDlq>(nameof(PedidoReenvioDlq.Situacao)));
            tabela.HasCheckConstraint("CK_PedidosReenvioDlq_ContagensNaoNegativas", "[Reenviadas] >= 0 AND [BloqueadasPeloLimite] >= 0 AND [ForaDoFiltro] >= 0");
        });
        pedido.Property(p => p.Id).ValueGeneratedNever();
        pedido.Property(p => p.Consumidor).HasMaxLength(PedidoReenvioDlq.TamanhoMaximoConsumidor);
        pedido.Property(p => p.TipoEvento).HasMaxLength(PedidoReenvioDlq.TamanhoMaximoTipoEvento);
        pedido.Property(p => p.TrechoMotivo).HasMaxLength(PedidoReenvioDlq.TamanhoMaximoTrechoMotivo);
        pedido.Property(p => p.MotivoFalha).HasMaxLength(PedidoReenvioDlq.TamanhoMaximoMotivoFalha);
        // Contexto W3C da requisição do Admin, ligado à execução do reenvio (REQ-16, R-04).
        pedido.Property<string?>(ContextoBancoDados.ContextoRastreio).HasMaxLength(Observabilidade.ContextoRastreio.Tamanho).IsUnicode(false);
        pedido.Property(p => p.Situacao).HasConversion<string>().HasMaxLength(ContextoBancoDados.TamanhoEnum);
        pedido.Ignore(p => p.Filtro);
        pedido.HasIndex(p => new { p.Situacao, p.RegistradoEm }).HasDatabaseName("IX_PedidosReenvioDlq_Situacao_RegistradoEm");
    }
}
