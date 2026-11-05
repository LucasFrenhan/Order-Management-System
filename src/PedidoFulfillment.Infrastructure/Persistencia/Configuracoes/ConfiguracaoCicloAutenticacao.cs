using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PedidoFulfillment.Infrastructure.Identidade;

namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

/// <summary>Sessões de autenticação (REQ-24, data-model).</summary>
internal sealed class ConfiguracaoSessaoAutenticacao : IEntityTypeConfiguration<SessaoAutenticacao>
{
    public void Configure(EntityTypeBuilder<SessaoAutenticacao> sessao)
    {
        sessao.ToTable("SessoesAutenticacao");
        sessao.HasKey(s => s.Id);
        sessao.Property(s => s.Id).ValueGeneratedNever();
        sessao.HasOne<Usuario>().WithMany().HasForeignKey(s => s.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        // Encerrar as sessões da conta (FR-011) e limpar as vencidas (R-07).
        sessao.HasIndex(s => s.UsuarioId);
        sessao.HasIndex(s => s.ExpiraEm);
    }
}

/// <summary>Tokens de renovação (REQ-24, data-model).</summary>
internal sealed class ConfiguracaoTokenRenovacao : IEntityTypeConfiguration<TokenRenovacao>
{
    public void Configure(EntityTypeBuilder<TokenRenovacao> token)
    {
        token.ToTable("TokensRenovacao");
        token.HasKey(t => t.HashToken);
        token.Property(t => t.HashToken).HasColumnType("binary(32)").HasMaxLength(TokenRenovacao.TamanhoHash).IsFixedLength();
        token.HasOne<SessaoAutenticacao>().WithMany().HasForeignKey(t => t.SessaoId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>Tokens de uso único (REQ-24, data-model).</summary>
internal sealed class ConfiguracaoTokenUsoUnico : IEntityTypeConfiguration<TokenUsoUnico>
{
    public void Configure(EntityTypeBuilder<TokenUsoUnico> token)
    {
        token.ToTable("TokensUsoUnico");
        token.HasKey(t => t.HashToken);
        token.Property(t => t.HashToken).HasColumnType("binary(32)").HasMaxLength(TokenRenovacao.TamanhoHash).IsFixedLength();
        token.Property(t => t.Finalidade).HasConversion<string>().HasMaxLength(ContextoBancoDados.TamanhoEnum).IsUnicode(false);
        token.HasOne<Usuario>().WithMany().HasForeignKey(t => t.UsuarioId).OnDelete(DeleteBehavior.Cascade);
        token.HasIndex(t => new { t.UsuarioId, t.Finalidade });
        token.HasIndex(t => t.ExpiraEm);
    }
}

/// <summary>A fila do envio de e-mail (REQ-24, data-model).</summary>
internal sealed class ConfiguracaoSolicitacaoEmail : IEntityTypeConfiguration<SolicitacaoEmail>
{
    public void Configure(EntityTypeBuilder<SolicitacaoEmail> solicitacao)
    {
        solicitacao.ToTable("SolicitacoesEmail");
        solicitacao.HasKey(s => s.Id);
        solicitacao.Property(s => s.Id).ValueGeneratedNever();
        solicitacao.Property(s => s.Tipo).HasConversion<string>().HasMaxLength(ContextoBancoDados.TamanhoEnum).IsUnicode(false);
        solicitacao.Property(s => s.Destino).HasMaxLength(SolicitacaoEmail.TamanhoDestino);
        // A rodada do Worker (R-03).
        solicitacao.HasIndex(s => s.ProximaTentativaEm);
    }
}
