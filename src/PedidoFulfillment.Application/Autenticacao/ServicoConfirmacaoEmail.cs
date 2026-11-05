using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Emails;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>Regras da confirmação de e-mail (REQ-24). O login não a exige (FR-020).</summary>
public sealed class ServicoConfirmacaoEmail(IFilaEmails fila, IGestorIdentidade gestor, ILogger<ServicoConfirmacaoEmail> logger) : IServicoConfirmacaoEmail
{
    // Única resposta: o motivo real nunca aparece.
    private static readonly Falha TokenInvalido = new(
        TipoFalha.Validacao, "Token inválido.", "O token é inválido ou expirou. Peça um novo e-mail.", CodigosErro.TokenInvalido);

    public async Task<Falha?> ConfirmarAsync(ConfirmacaoEmailRequestDTO confirmacao, CancellationToken cancellationToken)
    {
        if (await gestor.ConfirmarEmailAsync(GeradorTokenOpaco.Hash(confirmacao.Token), cancellationToken) is not { } usuarioId)
        {
            return TokenInvalido;
        }

        EventosAutenticacao.EmailConfirmado(logger, usuarioId);
        return null;
    }

    public Task PedirReenvioAsync(ReenvioConfirmacaoRequestDTO pedido, CancellationToken cancellationToken) =>
        fila.EnfileirarAsync(TipoEmail.ConfirmacaoEmail, pedido.Email, cancellationToken);
}
