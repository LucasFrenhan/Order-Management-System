using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>Confirmação de e-mail e reenvio (REQ-24).</summary>
public interface IServicoConfirmacaoEmail
{
    /// <summary>Confirma pelo token. Inválido, expirado ou usado devolve a mesma falha; sucesso devolve nulo.</summary>
    Task<Falha?> ConfirmarAsync(ConfirmacaoEmailRequestDTO confirmacao, CancellationToken cancellationToken);

    /// <summary>Só grava a solicitação, sem consultar a conta: quem decide se envia é o Worker (FR-019).</summary>
    Task PedirReenvioAsync(ReenvioConfirmacaoRequestDTO pedido, CancellationToken cancellationToken);
}
