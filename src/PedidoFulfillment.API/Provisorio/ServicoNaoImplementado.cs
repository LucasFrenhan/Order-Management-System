using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.API.Provisorio;

// Provisório (research R-05): remover no REQ-03/REQ-05, quando as implementações reais chegarem.
// Responde 501, que não faz parte do contrato v1.
public sealed class ServicoNaoImplementado : IServicoAutenticacao, IServicoPedidos
{
    private static readonly Falha NaoImplementado = new(TipoFalha.NaoImplementado, "Operação ainda não implementada.");

    public Task<Resultado<UsuarioResponseDTO>> RegistrarAsync(RegistrarUsuarioRequestDTO usuario, CancellationToken cancellationToken) =>
        Task.FromResult<Resultado<UsuarioResponseDTO>>(NaoImplementado);

    public Task<Resultado<LoginResponseDTO>> AutenticarAsync(LoginRequestDTO credenciais, CancellationToken cancellationToken) =>
        Task.FromResult<Resultado<LoginResponseDTO>>(NaoImplementado);

    public Task<Resultado<PedidoResponseDTO>> CriarAsync(Guid clienteId, Guid chaveIdempotencia, CriarPedidoRequestDTO pedido, CancellationToken cancellationToken) =>
        Task.FromResult<Resultado<PedidoResponseDTO>>(NaoImplementado);
}
