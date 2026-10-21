using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Pedidos;

/// <summary>
/// Operações de pedidos (criação implementada no REQ-05).
/// </summary>
public interface IServicoPedidos
{
    /// <summary>
    /// Cria o pedido com status Pendente para o cliente do token. Idempotente por
    /// (cliente, chave) por no mínimo 24 horas: mesma chave e mesmo conteúdo devolvem o pedido
    /// original; conteúdo divergente devolve <see cref="TipoFalha.Conflito"/>
    /// (<c>IDEMPOTENCIA_CONTEUDO_DIVERGENTE</c>); chave em processamento devolve
    /// <see cref="TipoFalha.Conflito"/> (<c>IDEMPOTENCIA_EM_PROCESSAMENTO</c>); produto
    /// inexistente ou indisponível devolve <see cref="TipoFalha.RegraNegocio"/>.
    /// </summary>
    Task<Resultado<PedidoResponseDTO>> CriarAsync(Guid clienteId, Guid chaveIdempotencia, CriarPedidoRequestDTO pedido, CancellationToken cancellationToken);

    /// <summary>
    /// Consulta um pedido. Admin e OperadorLogistica veem qualquer pedido; um Cliente só vê os
    /// próprios. Cliente que não é dono recebe <see cref="TipoFalha.NaoEncontrado"/>, para não
    /// revelar que o pedido existe.
    /// </summary>
    Task<Resultado<PedidoResponseDTO>> ObterAsync(Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken);
}
