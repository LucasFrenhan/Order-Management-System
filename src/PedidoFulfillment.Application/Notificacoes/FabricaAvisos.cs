using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Contratos.Notificacoes;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Notificacoes;

/// <summary>
/// Monta o <c>pedido.status-alterado</c> e os avisos de notificação (REQ-18, research R-14). Quem
/// grava (o <c>ArmazenamentoPedidos</c>) os adiciona à mesma gravação da mudança. O aviso leva o
/// mesmo identificador do evento de origem, que vira o <c>webhook-id</c> e o <c>id</c> do SSE.
/// </summary>
public static class FabricaAvisos
{
    /// <exception cref="ArgumentException">A entrada é a da criação do pedido (sem status anterior).</exception>
    public static (EventoSaida Evento, AvisoPedido Aviso) StatusAlterado(
        Pedido pedido, HistoricoStatusPedido entrada, int sequencia, OrigemMudancaStatus origem)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        ArgumentNullException.ThrowIfNull(entrada);
        var anterior = entrada.StatusAnterior
            ?? throw new ArgumentException("A criação do pedido não gera pedido.status-alterado (FR-001).", nameof(entrada));
        var instante = entrada.OcorridoEm;

        var envelope = EnvelopeEvento.Criar(pedido.Id, new PedidoStatusAlterado
        {
            PedidoId = pedido.Id,
            Numero = pedido.Numero,
            StatusAnterior = anterior,
            StatusNovo = entrada.StatusNovo,
            Sequencia = sequencia,
            Origem = origem,
            AlteradoEm = instante
        }, instante);
        var corpo = SerializacaoNotificacoes.Serializar(new CorpoNotificacao<DadosStatusAlterado>
        {
            IdNotificacao = envelope.IdEvento,
            Tipo = TiposNotificacao.StatusAlterado,
            Versao = TiposNotificacao.VersaoAtual,
            OcorridoEm = instante,
            Dados = new DadosStatusAlterado
            {
                PedidoId = pedido.Id,
                Numero = pedido.Numero,
                StatusAnterior = anterior,
                StatusNovo = entrada.StatusNovo,
                Sequencia = sequencia,
                Link = SerializacaoNotificacoes.Link(pedido.Id)
            }
        });

        return (EventoSaida.De(envelope, instante),
            AvisoPedido.Criar(envelope.IdEvento, TiposNotificacao.StatusAlterado, pedido.Id, pedido.ClienteId, corpo, instante));
    }

    /// <summary>Aviso do estorno confirmado, com o identificador do <c>pagamento.estornado</c> da mesma gravação (FR-004).</summary>
    public static AvisoPedido EstornoConfirmado(Pedido pedido, Guid idEventoEstornado, DateTimeOffset instante)
    {
        ArgumentNullException.ThrowIfNull(pedido);
        var corpo = SerializacaoNotificacoes.Serializar(new CorpoNotificacao<DadosEstornoConfirmado>
        {
            IdNotificacao = idEventoEstornado,
            Tipo = TiposNotificacao.EstornoConfirmado,
            Versao = TiposNotificacao.VersaoAtual,
            OcorridoEm = instante,
            Dados = new DadosEstornoConfirmado { PedidoId = pedido.Id, Numero = pedido.Numero, Link = SerializacaoNotificacoes.Link(pedido.Id) }
        });

        return AvisoPedido.Criar(idEventoEstornado, TiposNotificacao.EstornoConfirmado, pedido.Id, pedido.ClienteId, corpo, instante);
    }
}
