using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Autenticacao;

/// <summary>
/// Sessões e tokens no SQL Server (REQ-24, R-02). Os <c>UPDATE</c> condicionais decidem as disputas:
/// de duas apresentações simultâneas, só uma afeta a linha.
/// </summary>
internal sealed class ArmazenamentoSessoes(ContextoBancoDados contexto) : IArmazenamentoSessoes
{
    private static readonly FinalidadeTokenUsoUnico[] DefinemSenha = [FinalidadeTokenUsoUnico.RecuperacaoSenha, FinalidadeTokenUsoUnico.ConviteUsuario];

    public async Task<DateTimeOffset> CriarSessaoAsync(
        Guid usuarioId, byte[] hashToken, DateTimeOffset agora, ConfiguracaoSessoes validades, CancellationToken cancellationToken)
    {
        var sessao = new SessaoAutenticacao
        {
            Id = Guid.NewGuid(),
            UsuarioId = usuarioId,
            IniciadaEm = agora,
            ExpiraEm = agora + validades.DuracaoMaxima
        };
        var expiraEm = ValidadeToken(agora, validades.ValidadeToken, sessao.ExpiraEm);
        contexto.SessoesAutenticacao.Add(sessao);
        contexto.TokensRenovacao.Add(new TokenRenovacao { HashToken = hashToken, SessaoId = sessao.Id, ExpiraEm = expiraEm });
        await contexto.SaveChangesAsync(cancellationToken);
        return expiraEm;
    }

    public async Task<ResultadoRotacao> RotacionarAsync(
        byte[] hashApresentado, byte[] hashNovo, DateTimeOffset agora, ConfiguracaoSessoes validades, CancellationToken cancellationToken)
    {
        await using var transacao = await contexto.Database.BeginTransactionAsync(cancellationToken);
        var consumidos = await contexto.TokensRenovacao
            .Where(t => t.HashToken == hashApresentado && t.UsadoEm == null && t.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsadoEm, agora), cancellationToken);
        if (consumidos == 0)
        {
            var resultado = await TratarNaoConsumidoAsync(hashApresentado, agora, cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return resultado;
        }

        var sessao = await SessaoDoToken(hashApresentado).SingleAsync(cancellationToken);
        if (sessao.EncerradaEm is not null || sessao.ExpiraEm <= agora)
        {
            // Sai sem confirmar: a sessão acabou, e o token não precisa mudar.
            return new ResultadoRotacao(SituacaoRotacao.Recusada);
        }

        var expiraEm = ValidadeToken(agora, validades.ValidadeToken, sessao.ExpiraEm);
        contexto.TokensRenovacao.Add(new TokenRenovacao { HashToken = hashNovo, SessaoId = sessao.Id, ExpiraEm = expiraEm });
        await contexto.SaveChangesAsync(cancellationToken);
        await transacao.CommitAsync(cancellationToken);
        return new ResultadoRotacao(SituacaoRotacao.Renovada, sessao.UsuarioId, expiraEm);
    }

    public async Task<Guid?> EncerrarSessaoAsync(byte[] hashToken, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var sessao = await contexto.TokensRenovacao.AsNoTracking()
            .Where(t => t.HashToken == hashToken && t.UsadoEm == null && t.ExpiraEm > agora)
            .Join(contexto.SessoesAutenticacao, t => t.SessaoId, s => s.Id, (_, s) => new { s.Id, s.UsuarioId })
            .SingleOrDefaultAsync(cancellationToken);
        if (sessao is null)
        {
            return null;
        }

        return await EncerrarAsync(sessao.Id, agora, cancellationToken) ? sessao.UsuarioId : null;
    }

    public Task EncerrarSessoesDaContaAsync(Guid usuarioId, DateTimeOffset agora, CancellationToken cancellationToken) =>
        contexto.SessoesAutenticacao
            .Where(s => s.UsuarioId == usuarioId && s.EncerradaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EncerradaEm, agora), cancellationToken);

    public async Task CriarTokenUsoUnicoAsync(
        Guid usuarioId, FinalidadeTokenUsoUnico finalidade, byte[] hashToken, DateTimeOffset expiraEm, CancellationToken cancellationToken)
    {
        // Sem transação aberta por quem chamou, abre uma: nunca fica sem token válido nem com dois.
        await using var transacao = contexto.Database.CurrentTransaction is null
            ? await contexto.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await contexto.TokensUsoUnico
            .Where(t => t.UsuarioId == usuarioId && t.Finalidade == finalidade)
            .ExecuteDeleteAsync(cancellationToken);
        contexto.TokensUsoUnico.Add(new TokenUsoUnico { HashToken = hashToken, UsuarioId = usuarioId, Finalidade = finalidade, ExpiraEm = expiraEm });
        await contexto.SaveChangesAsync(cancellationToken);
        if (transacao is not null)
        {
            await transacao.CommitAsync(cancellationToken);
        }
    }

    public async Task<Guid?> ConsumirTokenUsoUnicoAsync(
        FinalidadeTokenUsoUnico finalidade, byte[] hashToken, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var consumidos = await contexto.TokensUsoUnico
            .Where(t => t.HashToken == hashToken && t.Finalidade == finalidade && t.UsadoEm == null && t.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsadoEm, agora), cancellationToken);
        if (consumidos == 0)
        {
            return null;
        }

        return await contexto.TokensUsoUnico.AsNoTracking()
            .Where(t => t.HashToken == hashToken)
            .Select(t => t.UsuarioId)
            .SingleAsync(cancellationToken);
    }

    public async Task<TokenSenhaConsumido?> ConsumirTokenSenhaAsync(byte[] hashToken, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var consumidos = await contexto.TokensUsoUnico
            .Where(t => t.HashToken == hashToken && DefinemSenha.Contains(t.Finalidade) && t.UsadoEm == null && t.ExpiraEm > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsadoEm, agora), cancellationToken);
        if (consumidos == 0)
        {
            return null;
        }

        var token = await contexto.TokensUsoUnico.AsNoTracking()
            .Where(t => t.HashToken == hashToken)
            .Select(t => new TokenSenhaConsumido(t.UsuarioId, t.Finalidade))
            .SingleAsync(cancellationToken);
        await contexto.TokensUsoUnico
            .Where(t => t.UsuarioId == token.UsuarioId && DefinemSenha.Contains(t.Finalidade) && t.HashToken != hashToken)
            .ExecuteDeleteAsync(cancellationToken);
        return token;
    }

    /// <summary>Só um token já usado é reutilização, e encerra a sessão (FR-004). Quem a encerrou é o único a relatar.</summary>
    private async Task<ResultadoRotacao> TratarNaoConsumidoAsync(byte[] hash, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var usado = await contexto.TokensRenovacao.AsNoTracking()
            .Where(t => t.HashToken == hash && t.UsadoEm != null)
            .Join(contexto.SessoesAutenticacao, t => t.SessaoId, s => s.Id, (_, s) => new { s.Id, s.UsuarioId })
            .SingleOrDefaultAsync(cancellationToken);
        return usado is not null && await EncerrarAsync(usado.Id, agora, cancellationToken)
            ? new ResultadoRotacao(SituacaoRotacao.Reutilizada, usado.UsuarioId)
            : new ResultadoRotacao(SituacaoRotacao.Recusada);
    }

    private async Task<bool> EncerrarAsync(Guid sessaoId, DateTimeOffset agora, CancellationToken cancellationToken) =>
        await contexto.SessoesAutenticacao
            .Where(s => s.Id == sessaoId && s.EncerradaEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EncerradaEm, agora), cancellationToken) == 1;

    private IQueryable<SessaoAutenticacao> SessaoDoToken(byte[] hash) =>
        contexto.TokensRenovacao.AsNoTracking()
            .Where(t => t.HashToken == hash)
            .Join(contexto.SessoesAutenticacao.AsNoTracking(), t => t.SessaoId, s => s.Id, (_, s) => s);

    /// <summary>Nunca passa do limite da sessão (data-model).</summary>
    private static DateTimeOffset ValidadeToken(DateTimeOffset agora, TimeSpan validade, DateTimeOffset limiteSessao)
    {
        var expiraEm = agora + validade;
        return expiraEm < limiteSessao ? expiraEm : limiteSessao;
    }
}
