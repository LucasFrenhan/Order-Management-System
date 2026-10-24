namespace PedidoFulfillment.Application.Reconciliacao;

/// <summary>Pedido candidato da reconciliação: a etapa em que parou e desde quando (REQ-12, R-05).</summary>
public sealed record PedidoParado(Guid PedidoId, EtapaFluxoPedido Etapa, DateTimeOffset AtualizadoEm);
