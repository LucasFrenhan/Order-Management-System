using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.API.Provisorio;

// Provisório (research R-05 do REQ-01): remover no REQ-05, quando o serviço de pedidos chegar.
// Responde 501, que não faz parte do contrato v1.
public sealed class ServicoNaoImplementado : IServicoPedidos
{
    private static readonly Falha NaoImplementado = new(TipoFalha.NaoImplementado, "Operação ainda não implementada.");

    public Task<Resultado<PedidoResponseDTO>> CriarAsync(Guid clienteId, Guid chaveIdempotencia, CriarPedidoRequestDTO pedido, CancellationToken cancellationToken) =>
        Task.FromResult<Resultado<PedidoResponseDTO>>(NaoImplementado);

    public Task<Resultado<PedidoResponseDTO>> ObterAsync(Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken) =>
        Task.FromResult<Resultado<PedidoResponseDTO>>(NaoImplementado);

    public Task<Resultado<PedidoResponseDTO>> AtualizarStatusAsync(
        Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, AtualizarStatusPedidoRequestDTO atualizacao, CancellationToken cancellationToken) =>
        Task.FromResult<Resultado<PedidoResponseDTO>>(NaoImplementado);
}
