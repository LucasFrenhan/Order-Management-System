using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>Recuperação e redefinição de senha (REQ-24).</summary>
public interface IServicoSenhas
{
    /// <summary>Só grava a solicitação, sem consultar a conta: a resposta e o tempo não a revelam (FR-013).</summary>
    Task PedirRecuperacaoAsync(RecuperacaoSenhaRequestDTO pedido, CancellationToken cancellationToken);

    /// <summary>Troca a senha pelo token. Inválido, expirado ou usado devolve a mesma falha (FR-016); sucesso devolve nulo.</summary>
    Task<Falha?> RedefinirAsync(RedefinicaoSenhaRequestDTO redefinicao, CancellationToken cancellationToken);
}
