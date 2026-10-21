using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>
/// Regras de registro e login (research R-01): aparo, respostas do contrato e eventos de
/// segurança com e-mail mascarado (FR-019).
/// </summary>
public sealed class ServicoAutenticacao(IGestorIdentidade gestor, IEmissorToken emissor, ILogger<ServicoAutenticacao> logger)
    : IServicoAutenticacao
{
    // Única resposta para e-mail inexistente, senha errada e conta bloqueada (FR-008, FR-009).
    private static readonly Falha CredenciaisInvalidas = new(TipoFalha.NaoAutenticado, "Credenciais inválidas.", "E-mail ou senha incorretos.");

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

    public async Task<Resultado<LoginResponseDTO>> AutenticarAsync(LoginRequestDTO credenciais, CancellationToken cancellationToken)
    {
        var email = credenciais.Email.Trim();
        var verificacao = await gestor.VerificarCredenciaisAsync(email, credenciais.Senha, cancellationToken);
        if (verificacao is { Situacao: SituacaoCredenciais.Valida, UsuarioId: { } usuarioId })
        {
            EventosAutenticacao.LoginRealizado(logger, usuarioId);
            return emissor.Emitir(usuarioId, verificacao.Perfis);
        }

        var emailMascarado = MascaraEmail.Mascarar(email);
        var motivo = verificacao.Situacao == SituacaoCredenciais.Bloqueada ? "ContaBloqueada" : "CredenciaisInvalidas";
        EventosAutenticacao.LoginRecusado(logger, emailMascarado, verificacao.UsuarioId, motivo);
        if (verificacao is { Situacao: SituacaoCredenciais.BloqueadaAgora, UsuarioId: { } bloqueadoId })
        {
            EventosAutenticacao.ContaBloqueada(logger, bloqueadoId, emailMascarado);
        }

        return CredenciaisInvalidas;
    }
}
