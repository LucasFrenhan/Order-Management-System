using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Usuarios;
using PedidoFulfillment.Application.Emails;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Application.Usuarios;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Identidade;

/// <summary>
/// Gestão de contas e perfis (REQ-25). Toda alteração roda em uma transação que começa com um lock de
/// aplicação único: contar os Admins e depois retirar um não é atômico, e sem o lock dois Admins
/// removeriam um ao outro (R-03). Situação e perfis são gravados sem passar pelo <c>UserManager</c>, que
/// confere o <c>ConcurrencyStamp</c> e falharia por um login simultâneo (R-01, R-09).
/// </summary>
internal sealed class GestorUsuarios(
    UserManager<Usuario> usuarios,
    ContextoBancoDados contexto,
    IArmazenamentoSessoes sessoes,
    IFilaEmails fila,
    TimeProvider relogio) : IGestorUsuarios
{
    internal const string RecursoLock = "oms-gestao-usuarios";
    internal const int EsperaLockMilissegundos = 10_000;

    /// <summary>Ignora maiúsculas e acentos em qualquer servidor (R-07). A busca já lê a tabela inteira, então não perde índice.</summary>
    private const string CollationBusca = "Latin1_General_100_CI_AI";

    public Task<bool> EhAdminAtivoAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        AdminsAtivos().AnyAsync(u => u.Id == usuarioId, cancellationToken);

    public async Task<ContaGestao?> ObterAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var contas = await MontarAsync(contexto.Users.AsNoTracking().Where(u => u.Id == usuarioId), cancellationToken);
        return contas.Count == 0 ? null : contas[0];
    }

    public async Task<IReadOnlyList<ContaGestao>> ListarAsync(
        FiltroListagemUsuarios filtro, PosicaoCursorUsuarios? posicao, int quantidade, CancellationToken cancellationToken)
    {
        var contas = contexto.Users.AsNoTracking();
        if (filtro.Perfil is { } perfil)
        {
            contas = ComPerfil(contas, perfil);
        }

        contas = filtro.Situacao switch
        {
            SituacaoUsuario.Ativa => contas.Where(u => u.DesativadoEm == null),
            SituacaoUsuario.Desativada => contas.Where(u => u.DesativadoEm != null),
            _ => contas
        };

        if (filtro.Busca is { } busca)
        {
            contas = contas.Where(u => EF.Functions.Collate(u.Nome, CollationBusca).Contains(busca)
                || EF.Functions.Collate(u.Email!, CollationBusca).Contains(busca));
        }

        // A ordem da listagem é crescente (Nome, Id): "depois" é maior e "antes" é menor.
        if (posicao is { Sentido: SentidoCursor.Anterior } anterior)
        {
            var (n, i) = (anterior.Nome, anterior.Id);
            contas = anterior.Inclusiva
                ? contas.Where(u => string.Compare(u.Nome, n) < 0 || (u.Nome == n && u.Id.CompareTo(i) <= 0))
                : contas.Where(u => string.Compare(u.Nome, n) < 0 || (u.Nome == n && u.Id.CompareTo(i) < 0));
            var lidas = await MontarAsync(contas.OrderByDescending(u => u.Nome).ThenByDescending(u => u.Id).Take(quantidade), cancellationToken);
            lidas.Reverse();
            return lidas;
        }

        if (posicao is { } proxima)
        {
            var (n, i) = (proxima.Nome, proxima.Id);
            contas = proxima.Inclusiva
                ? contas.Where(u => string.Compare(u.Nome, n) > 0 || (u.Nome == n && u.Id.CompareTo(i) >= 0))
                : contas.Where(u => string.Compare(u.Nome, n) > 0 || (u.Nome == n && u.Id.CompareTo(i) > 0));
        }

        return await MontarAsync(contas.OrderBy(u => u.Nome).ThenBy(u => u.Id).Take(quantidade), cancellationToken);
    }

    public async Task<Resultado<ContaGestao>> CriarAsync(Guid atorId, string nome, string email, PerfilUsuario perfil, CancellationToken cancellationToken)
    {
        var usuario = new Usuario { UserName = email, Email = email, Nome = nome };
        await using var transacao = await IniciarSerializadaAsync(cancellationToken);
        if (!await EhAdminAtivoAsync(atorId, cancellationToken))
        {
            return RegrasGestaoUsuarios.SemPermissao;
        }

        try
        {
            var criacao = await usuarios.CreateAsync(usuario);
            if (!criacao.Succeeded)
            {
                return criacao.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.DuplicateEmail))
                    ? GestorIdentidade.EmailJaCadastrado
                    : throw GestorIdentidade.Inesperado(criacao);
            }

            GestorIdentidade.Garantir(await usuarios.AddToRoleAsync(usuario, perfil.ToString()));
            await fila.EnfileirarAsync(TipoEmail.ConviteUsuario, email, cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (ErrosBancoDados.EhViolacaoUnicidade(excecao))
        {
            // Passou pela checagem do Identity e esbarrou no índice único, como no registro.
            return GestorIdentidade.EmailJaCadastrado;
        }

        return new ContaGestao(usuario.Id, nome, email, [perfil], Desativada: false, EmailConfirmado: false, ConvitePendente: true);
    }

    public async Task<ResultadoAlteracaoUsuario> AlterarAsync(Guid atorId, Guid alvoId, AlteracaoUsuario alteracao, CancellationToken cancellationToken)
    {
        await using var transacao = await IniciarSerializadaAsync(cancellationToken);
        var cenario = new CenarioAlteracao(
            atorId,
            await EhAdminAtivoAsync(atorId, cancellationToken),
            await ObterAsync(alvoId, cancellationToken),
            await AdminsAtivos().CountAsync(cancellationToken));

        var decisao = RegrasGestaoUsuarios.Avaliar(cenario, alteracao);
        switch (decisao.Desfecho)
        {
            case DesfechoAlteracao.Recusar:
                return new ResultadoAlteracaoUsuario(decisao, null);
            case DesfechoAlteracao.SemEfeito:
                return new ResultadoAlteracaoUsuario(decisao, cenario.Alvo);
            default:
                await AplicarAsync(alvoId, alteracao, cancellationToken);
                var conta = await ObterAsync(alvoId, cancellationToken);
                await transacao.CommitAsync(cancellationToken);
                return new ResultadoAlteracaoUsuario(decisao, conta);
        }
    }

    private async Task AplicarAsync(Guid alvoId, AlteracaoUsuario alteracao, CancellationToken cancellationToken)
    {
        var agora = relogio.GetUtcNow();
        switch (alteracao.Tipo)
        {
            case TipoAlteracaoUsuario.AtribuirPerfil:
                var perfilId = await PerfilIdAsync(alteracao.Perfil!.Value, cancellationToken);
                contexto.UserRoles.Add(new IdentityUserRole<Guid> { UserId = alvoId, RoleId = perfilId });
                await contexto.SaveChangesAsync(cancellationToken);
                break;
            case TipoAlteracaoUsuario.RetirarPerfil:
                var retirado = await PerfilIdAsync(alteracao.Perfil!.Value, cancellationToken);
                await contexto.UserRoles
                    .Where(p => p.UserId == alvoId && p.RoleId == retirado)
                    .ExecuteDeleteAsync(cancellationToken);
                break;
            case TipoAlteracaoUsuario.Desativar:
                await contexto.Users.Where(u => u.Id == alvoId)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.DesativadoEm, agora), cancellationToken);
                await sessoes.EncerrarSessoesDaContaAsync(alvoId, agora, cancellationToken);
                await contexto.TokensUsoUnico.Where(t => t.UsuarioId == alvoId).ExecuteDeleteAsync(cancellationToken);
                break;
            case TipoAlteracaoUsuario.Reativar:
                await contexto.Users.Where(u => u.Id == alvoId).ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.DesativadoEm, (DateTimeOffset?)null)
                    .SetProperty(u => u.ReativadoEm, agora), cancellationToken);
                // Um login simultâneo à desativação pode ter criado uma sessão depois de as demais serem encerradas (R-08).
                await sessoes.EncerrarSessoesDaContaAsync(alvoId, agora, cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(alteracao));
        }
    }

    /// <summary>Com <c>@LockOwner = 'Transaction'</c>, o lock termina com a transação, em qualquer desfecho (R-03).</summary>
    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> IniciarSerializadaAsync(CancellationToken cancellationToken)
    {
        var transacao = await contexto.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var resultado = await contexto.Database.SqlQuery<int>($"""
                DECLARE @r int;
                EXEC @r = sp_getapplock @Resource = {RecursoLock}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = {EsperaLockMilissegundos};
                SELECT @r AS Value;
                """).ToListAsync(cancellationToken);
            return resultado[0] >= 0
                ? transacao
                : throw new InvalidOperationException($"Não foi possível obter o lock da gestão de usuários (código {resultado[0]}).");
        }
        catch
        {
            await transacao.DisposeAsync();
            throw;
        }
    }

    private IQueryable<Usuario> AdminsAtivos() =>
        ComPerfil(contexto.Users.AsNoTracking(), PerfilUsuario.Admin)
            .Where(u => u.DesativadoEm == null && u.PasswordHash != null);

    private IQueryable<Usuario> ComPerfil(IQueryable<Usuario> contas, PerfilUsuario perfil)
    {
        var nome = perfil.ToString();
        return contas.Where(u => contexto.UserRoles.Any(p => p.UserId == u.Id
            && contexto.Roles.Any(r => r.Id == p.RoleId && r.Name == nome)));
    }

    private Task<Guid> PerfilIdAsync(PerfilUsuario perfil, CancellationToken cancellationToken)
    {
        var nome = perfil.ToString();
        return contexto.Roles.AsNoTracking().Where(r => r.Name == nome).Select(r => r.Id).SingleAsync(cancellationToken);
    }

    /// <summary>A projeção deixa de fora senha, hash e carimbo: eles nunca saem desta classe, só o "tem senha" (FR-006).</summary>
    private async Task<List<ContaGestao>> MontarAsync(IQueryable<Usuario> consulta, CancellationToken cancellationToken)
    {
        var linhas = await consulta
            .Select(u => new { u.Id, u.Nome, u.Email, u.DesativadoEm, u.EmailConfirmed, TemSenha = u.PasswordHash != null })
            .ToListAsync(cancellationToken);
        var ids = linhas.Select(l => l.Id).ToList();
        var perfis = await contexto.UserRoles.AsNoTracking()
            .Where(p => ids.Contains(p.UserId))
            .Join(contexto.Roles.AsNoTracking(), p => p.RoleId, r => r.Id, (p, r) => new { p.UserId, r.Name })
            .ToListAsync(cancellationToken);
        var porConta = perfis.ToLookup(p => p.UserId, p => Enum.Parse<PerfilUsuario>(p.Name!));
        return [.. linhas.Select(l => new ContaGestao(
            l.Id, l.Nome, l.Email!, [.. porConta[l.Id].Order()], l.DesativadoEm is not null, l.EmailConfirmed, !l.TemSenha))];
    }
}
