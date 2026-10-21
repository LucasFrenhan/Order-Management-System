using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.API.Provisorio;

// Provisório (research R-05): remover no REQ-03/REQ-05, quando as implementações reais chegarem.
// Responde 501, que não faz parte do contrato v1.
public sealed class ServicoNaoImplementado : IServicoAutenticacao, IServicoPedidos
{
    private static readonly Falha NaoImplementado = new(TipoFalha.NaoImplementado, "Operação ainda não implementada.");
}
