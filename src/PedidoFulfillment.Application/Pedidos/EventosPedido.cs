using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Pedidos;

/// <summary>
/// Monta o <c>pedido.cancelado</c> de todos os caminhos de cancelamento (REQ-15, R-02): a API, a
/// falta de estoque e a reconciliação. Quem cancela adiciona o evento à mesma gravação.
/// </summary>
public static class EventosPedido
{
    /// <exception cref="ArgumentException">
    /// A etapa não combina com o motivo: ela é obrigatória com <see cref="MotivoCancelamento.ReconciliacaoEsgotada"/> e
    /// proibida nos demais, como no schema.
    /// </exception>
    public static EventoSaida Cancelado(
        Pedido pedido, MotivoCancelamento motivo, EtapaEsgotada? etapa, ResultadoCancelamento resultado, DateTimeOffset instante)
    {
        if ((motivo == MotivoCancelamento.ReconciliacaoEsgotada) != etapa.HasValue)
        {
            throw new ArgumentException(
                $"A etapa esgotada ({etapa?.ToString() ?? "nula"}) não combina com o motivo {motivo}.", nameof(etapa));
        }

        return EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new PedidoCancelado
        {
            PedidoId = pedido.Id,
            Numero = pedido.Numero,
            Motivo = motivo,
            EtapaEsgotada = etapa,
            CanceladoEm = instante,
            EstoqueDevolvido = resultado.EstoqueDevolvido,
            EstornoPendente = resultado.EstornoSolicitado,
            OrdemEntregaCancelada = resultado.OrdemCancelada
        }, instante), instante);
    }
}
