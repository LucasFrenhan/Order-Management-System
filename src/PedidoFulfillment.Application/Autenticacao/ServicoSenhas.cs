using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Emails;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>
/// Regras de recuperação e redefinição de senha (REQ-24, R-09). O pedido não gera log: a API nem sabe
/// se a conta existe, e quem registra é o Worker, ao enviar (R-10).
/// </summary>
public sealed class ServicoSenhas(IFilaEmails fila, IGestorIdentidade gestor, ILogger<ServicoSenhas> logger) : IServicoSenhas
{
    // Única resposta: o motivo real nunca aparece (FR-016).
    private static readonly Falha TokenInvalido = new(
        TipoFalha.Validacao, "Token inválido.", "O token é inválido ou expirou. Peça uma nova recuperação.", CodigosErro.TokenInvalido);

    public Task PedirRecuperacaoAsync(RecuperacaoSenhaRequestDTO pedido, CancellationToken cancellationToken) =>
        fila.EnfileirarAsync(TipoEmail.RecuperacaoSenha, pedido.Email, cancellationToken);

    public async Task<Falha?> RedefinirAsync(RedefinicaoSenhaRequestDTO redefinicao, CancellationToken cancellationToken)
    {
        var usuarioId = await gestor.RedefinirSenhaAsync(GeradorTokenOpaco.Hash(redefinicao.Token), redefinicao.NovaSenha, cancellationToken);
        if (usuarioId is not { } id)
        {
            return TokenInvalido;
        }

        EventosAutenticacao.SenhaRedefinida(logger, id);
        return null;
    }

    public async Task<Falha?> TrocarAsync(Guid usuarioId, TrocaSenhaRequestDTO troca, CancellationToken cancellationToken)
    {
        switch (await gestor.TrocarSenhaAsync(usuarioId, troca.SenhaAtual, troca.NovaSenha, cancellationToken))
        {
            case ResultadoTrocaSenha.Trocada:
                EventosAutenticacao.SenhaTrocada(logger, usuarioId);
                return null;
            case ResultadoTrocaSenha.MesmaSenha:
                return ErroNoCampo("novaSenha", "A nova senha deve ser diferente da atual.");
            default:
                EventosAutenticacao.TrocaSenhaRecusada(logger, usuarioId);
                return ErroNoCampo("senhaAtual", "A senha atual está incorreta.");
        }
    }

    private static Falha ErroNoCampo(string campo, string mensagem) =>
        new(TipoFalha.Validacao, "Dados de entrada inválidos.", Erros: new Dictionary<string, string[]> { [campo] = [mensagem] });
}
