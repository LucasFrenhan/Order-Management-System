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
}
