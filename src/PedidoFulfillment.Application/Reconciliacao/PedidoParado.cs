namespace PedidoFulfillment.Application.Reconciliacao;

/// <summary>
/// Pedido candidato da reconciliação: a etapa em que parou e desde quando (REQ-12, R-05). O
/// contexto de rastreamento da criação (REQ-16, R-05) é opaco aqui: só liga o passo ao pedido.
/// </summary>
public sealed record PedidoParado(Guid PedidoId, EtapaFluxoPedido Etapa, DateTimeOffset AtualizadoEm, string? ContextoRastreio = null);
