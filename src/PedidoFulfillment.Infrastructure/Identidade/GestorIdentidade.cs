using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Identidade;

/// <summary>
/// Contas e credenciais com o <see cref="UserManager{TUser}"/> do Identity (research R-02 a R-05).
/// </summary>
internal sealed class GestorIdentidade(UserManager<Usuario> usuarios, ContextoBancoDados contexto) : IGestorIdentidade
{
    private static readonly Falha EmailJaCadastrado = new(
        TipoFalha.Conflito, "E-mail já cadastrado.", "Já existe uma conta com este e-mail.", CodigosErro.UsuarioEmailJaCadastrado);

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

            await transacao.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (ErrosBancoDados.EhViolacaoUnicidade(excecao))
        {
            // Registro simultâneo: passou pela checagem do Identity e esbarrou no índice único.
            return EmailJaCadastrado;
        }

        return new UsuarioResponseDTO { Id = usuario.Id, Nome = nome, Email = email, Perfis = [PerfilUsuario.Cliente] };
    }

    public Task<ResultadoCredenciais> VerificarCredenciaisAsync(string email, string senha, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Login real chega na T028 (REQ-03).");

    // Só os códigos do Identity: as descrições podem repetir o e-mail informado.
    private static InvalidOperationException Inesperado(IdentityResult resultado) =>
        new($"O Identity recusou a operação: {string.Join(", ", resultado.Errors.Select(e => e.Code))}.");
}
