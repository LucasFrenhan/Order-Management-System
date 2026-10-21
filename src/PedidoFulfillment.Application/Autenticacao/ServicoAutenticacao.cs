using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>
/// Regras de registro e login (research R-01): aparo, respostas do contrato e eventos de
/// segurança com e-mail mascarado (FR-019).
/// </summary>
public sealed class ServicoAutenticacao(IGestorIdentidade gestor, ILogger<ServicoAutenticacao> logger)
    : IServicoAutenticacao
{
    public async Task<Resultado<UsuarioResponseDTO>> RegistrarAsync(RegistrarUsuarioRequestDTO usuario, CancellationToken cancellationToken)
    {
        // O contrato aceita espaços nas pontas do e-mail; sem aparo eles furariam a unicidade (FR-004).
        var resultado = await gestor.CriarAsync(usuario.Nome.Trim(), usuario.Email.Trim(), usuario.Senha, cancellationToken);
        if (resultado.Valor is { } criado)
        {
            EventosAutenticacao.UsuarioRegistrado(logger, criado.Id, MascaraEmail.Mascarar(criado.Email));
        }

        return resultado;
    }

    public Task<Resultado<LoginResponseDTO>> AutenticarAsync(LoginRequestDTO credenciais, CancellationToken cancellationToken) =>
        Task.FromResult<Resultado<LoginResponseDTO>>(new Falha(TipoFalha.NaoImplementado, "Operação ainda não implementada."));
}
