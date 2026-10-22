using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Pedidos;

/// <summary>
/// Operações de pedidos, implementadas no REQ-05 (<c>ServicoPedidos</c>).
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

    /// <summary>
    /// Altera o status conforme as transições permitidas (FR-020):
    /// <list type="table">
    /// <listheader><term>De → Para</term><description>Perfis</description></listheader>
    /// <item><term>Pendente → Processando</term><description>Admin, OperadorLogistica</description></item>
    /// <item><term>Processando → ProntoParaEnvio</term><description>Admin, OperadorLogistica</description></item>
    /// <item><term>ProntoParaEnvio → Despachado</term><description>Admin, OperadorLogistica</description></item>
    /// <item><term>Pendente, Processando, ProntoParaEnvio → Cancelado</term><description>Admin, OperadorLogistica</description></item>
    /// <item><term>Pendente → Cancelado</term><description>Cliente dono do pedido</description></item>
    /// </list>
    /// Outra transição (inclusive para o mesmo status): <see cref="TipoFalha.Conflito"/> com
    /// <c>PEDIDO_TRANSICAO_INVALIDA</c>. Cliente em pedido de terceiro: <see cref="TipoFalha.NaoEncontrado"/>.
    /// Cliente em transição diferente de Pendente → Cancelado: <see cref="TipoFalha.SemPermissao"/>.
    /// </summary>
    Task<Resultado<PedidoResponseDTO>> AtualizarStatusAsync(
        Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, AtualizarStatusPedidoRequestDTO atualizacao, CancellationToken cancellationToken);
}
