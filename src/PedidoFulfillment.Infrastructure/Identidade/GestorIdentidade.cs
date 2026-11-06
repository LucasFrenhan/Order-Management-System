using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Emails;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Identidade;

/// <summary>
/// Contas e credenciais com o <see cref="UserManager{TUser}"/> do Identity (research R-02 a R-05).
/// </summary>
internal sealed class GestorIdentidade(
    UserManager<Usuario> usuarios,
    ContextoBancoDados contexto,
    IArmazenamentoSessoes sessoes,
    IFilaEmails fila,
    TimeProvider relogio) : IGestorIdentidade
{
    internal static readonly Falha EmailJaCadastrado = new(
        TipoFalha.Conflito, "E-mail já cadastrado.", "Já existe uma conta com este e-mail.", CodigosErro.UsuarioEmailJaCadastrado);

    // Hash de uma senha aleatória, gerado uma vez, para verificar quando o e-mail não existe.
    private static string? hashReferencia;

    public async Task<Resultado<UsuarioResponseDTO>> CriarAsync(string nome, string email, string senha, CancellationToken cancellationToken)
    {
        var usuario = new Usuario { UserName = email, Email = email, Nome = nome };
        // Conta e perfil juntos: nunca fica conta sem perfil (research R-03).
        await using var transacao = await contexto.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var criacao = await usuarios.CreateAsync(usuario, senha);
            if (!criacao.Succeeded)
            {
                return criacao.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.DuplicateEmail))
                    ? EmailJaCadastrado
                    : throw Inesperado(criacao);
            }

            var perfil = await usuarios.AddToRoleAsync(usuario, nameof(PerfilUsuario.Cliente));
            if (!perfil.Succeeded)
            {
                throw Inesperado(perfil);
            }

            // Na mesma transação: sem conta, nenhum e-mail; e a resposta do registro não muda (FR-017).
            await fila.EnfileirarAsync(TipoEmail.ConfirmacaoEmail, email, cancellationToken);
            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (ErrosBancoDados.EhViolacaoUnicidade(excecao))
        {
            // Registro simultâneo: passou pela checagem do Identity e esbarrou no índice único.
            return EmailJaCadastrado;
        }

        return new UsuarioResponseDTO { Id = usuario.Id, Nome = nome, Email = email, Perfis = [PerfilUsuario.Cliente] };
    }

    public async Task<ResultadoCredenciais> VerificarCredenciaisAsync(string email, string senha, CancellationToken cancellationToken)
    {
        var usuario = await usuarios.FindByEmailAsync(email);
        if (usuario is null)
        {
            // Mesmo custo de uma senha errada: o tempo não revela se o e-mail tem conta (research R-05).
            usuarios.PasswordHasher.VerifyHashedPassword(new Usuario(), HashReferencia(), senha);
            return ResultadoCredenciais.Invalida(null);
        }

        // Desativada, ou sem senha (o CheckPassword recusaria sem calcular hash, o que revelaria a conta pelo
        // tempo, e contaria erro para o bloqueio): o mesmo custo de um e-mail inexistente, sem contar erro (REQ-25, R-08).
        if (usuario.DesativadoEm is not null || usuario.PasswordHash is null)
        {
            usuarios.PasswordHasher.VerifyHashedPassword(new Usuario(), HashReferencia(), senha);
            return usuario.DesativadoEm is not null ? ResultadoCredenciais.Desativada(usuario.Id) : ResultadoCredenciais.Invalida(usuario.Id);
        }

        // Em bloqueio, nem a senha é verificada (FR-009).
        if (await usuarios.IsLockedOutAsync(usuario))
        {
            return ResultadoCredenciais.Bloqueada(usuario.Id);
        }

        if (!await usuarios.CheckPasswordAsync(usuario, senha))
        {
            await usuarios.AccessFailedAsync(usuario);
            return await usuarios.IsLockedOutAsync(usuario)
                ? ResultadoCredenciais.BloqueadaAgora(usuario.Id)
                : ResultadoCredenciais.Invalida(usuario.Id);
        }

        if (usuario.AccessFailedCount > 0)
        {
            await usuarios.ResetAccessFailedCountAsync(usuario);
        }

        return ResultadoCredenciais.Valida(usuario.Id, await PerfisAsync(usuario));
    }

    public async Task<IReadOnlyList<PerfilUsuario>> ObterPerfisAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        await usuarios.FindByIdAsync(usuarioId.ToString()) is { } usuario ? await PerfisAsync(usuario) : [];

    public async Task<RedefinicaoConcluida?> RedefinirSenhaAsync(byte[] hashToken, string novaSenha, CancellationToken cancellationToken)
    {
        var agora = relogio.GetUtcNow();
        await using var transacao = await contexto.Database.BeginTransactionAsync(cancellationToken);
        // Sair sem confirmar desfaz o consumo. A desativação já apaga os tokens; isto cobre o gerado no mesmo instante (R-08).
        if (await sessoes.ConsumirTokenSenhaAsync(hashToken, agora, cancellationToken) is not { } token
            || await usuarios.FindByIdAsync(token.UsuarioId.ToString()) is not { DesativadoEm: null } usuario)
        {
            return null;
        }

        var peloConvite = token.Finalidade == FinalidadeTokenUsoUnico.ConviteUsuario;

        usuario.PasswordHash = usuarios.PasswordHasher.HashPassword(usuario, novaSenha);
        usuario.EmailConfirmed = true;
        usuario.AccessFailedCount = 0;
        usuario.LockoutEnd = null;
        Garantir(await usuarios.UpdateSecurityStampAsync(usuario));
        await sessoes.EncerrarSessoesDaContaAsync(usuario.Id, agora, cancellationToken);
        // Quem acabou de aceitar o convite não precisa de um aviso de senha redefinida.
        if (!peloConvite)
        {
            await fila.EnfileirarAsync(TipoEmail.AvisoSenhaRedefinida, usuario.Email!, cancellationToken);
        }

        await transacao.CommitAsync(cancellationToken);
        return new RedefinicaoConcluida(usuario.Id, peloConvite);
    }

    public async Task<ResultadoTrocaSenha> TrocarSenhaAsync(Guid usuarioId, string senhaAtual, string novaSenha, CancellationToken cancellationToken)
    {
        // Conta desativada, com um token de acesso ainda válido: sem verificar a senha nem contar erro (REQ-25, FR-018).
        if (await usuarios.FindByIdAsync(usuarioId.ToString()) is not { DesativadoEm: null } usuario || await usuarios.IsLockedOutAsync(usuario))
        {
            return ResultadoTrocaSenha.SenhaAtualIncorreta;
        }

        if (!await usuarios.CheckPasswordAsync(usuario, senhaAtual))
        {
            await usuarios.AccessFailedAsync(usuario);
            return ResultadoTrocaSenha.SenhaAtualIncorreta;
        }

        if (senhaAtual == novaSenha)
        {
            return ResultadoTrocaSenha.MesmaSenha;
        }

        await using var transacao = await contexto.Database.BeginTransactionAsync(cancellationToken);
        Garantir(await usuarios.ChangePasswordAsync(usuario, senhaAtual, novaSenha));
        Garantir(await usuarios.ResetAccessFailedCountAsync(usuario));
        await sessoes.EncerrarSessoesDaContaAsync(usuarioId, relogio.GetUtcNow(), cancellationToken);
        await transacao.CommitAsync(cancellationToken);
        return ResultadoTrocaSenha.Trocada;
    }

    public async Task<Guid?> ConfirmarEmailAsync(byte[] hashToken, CancellationToken cancellationToken)
    {
        await using var transacao = await contexto.Database.BeginTransactionAsync(cancellationToken);
        if (await sessoes.ConsumirTokenUsoUnicoAsync(FinalidadeTokenUsoUnico.ConfirmacaoEmail, hashToken, relogio.GetUtcNow(), cancellationToken) is not { } usuarioId
            || await usuarios.FindByIdAsync(usuarioId.ToString()) is not { DesativadoEm: null } usuario)
        {
            return null;
        }

        usuario.EmailConfirmed = true;
        Garantir(await usuarios.UpdateAsync(usuario));
        await transacao.CommitAsync(cancellationToken);
        return usuarioId;
    }

    private async Task<PerfilUsuario[]> PerfisAsync(Usuario usuario) =>
        [.. (await usuarios.GetRolesAsync(usuario))
            .Select(nome => Enum.TryParse<PerfilUsuario>(nome, out var perfil) ? perfil : (PerfilUsuario?)null)
            .OfType<PerfilUsuario>()];

    private string HashReferencia() =>
        hashReferencia ??= usuarios.PasswordHasher.HashPassword(new Usuario(), Guid.NewGuid().ToString());

    internal static void Garantir(IdentityResult resultado)
    {
        if (!resultado.Succeeded)
        {
            throw Inesperado(resultado);
        }
    }

    // Só os códigos do Identity: as descrições podem repetir o e-mail informado.
    internal static InvalidOperationException Inesperado(IdentityResult resultado) =>
        new($"O Identity recusou a operação: {string.Join(", ", resultado.Errors.Select(e => e.Code))}.");
}
