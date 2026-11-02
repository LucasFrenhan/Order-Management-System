using System.Text.Json;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Contratos.Notificacoes;

/// <summary>
/// Tipos do corpo <c>notificacao.v1</c> (REQ-18, R-09). Schema em
/// <c>specs/018-notificacao-status/contracts/notificacoes/notificacao.v1.schema.json</c>.
/// </summary>
public static class TiposNotificacao
{
    public const string StatusAlterado = "pedido.status-alterado";
    public const string EstornoConfirmado = "pedido.estorno-confirmado";

    public const int VersaoAtual = 1;
}

/// <summary>
/// Corpo do webhook e <c>data</c> do evento SSE (REQ-18, FR-002, FR-004 e FR-021). Sem dados
/// pessoais, valores, itens, pagamento ou rastreio: o detalhe se consulta no <c>link</c>.
/// </summary>
public sealed record CorpoNotificacao<TDados>
{
    /// <summary>Igual ao <c>idEvento</c> do evento de origem; <c>webhook-id</c> e <c>id</c> do SSE.</summary>
    public required Guid IdNotificacao { get; init; }

    public required string Tipo { get; init; }

    public required int Versao { get; init; }

    public required DateTimeOffset OcorridoEm { get; init; }

    public required TDados Dados { get; init; }
}

/// <summary>Dados de <c>pedido.status-alterado</c>.</summary>
public sealed record DadosStatusAlterado
{
    public required Guid PedidoId { get; init; }

    public required string Numero { get; init; }

    public required StatusPedido StatusAnterior { get; init; }

    public required StatusPedido StatusNovo { get; init; }

    public required int Sequencia { get; init; }

    public required string Link { get; init; }
}

/// <summary>Dados de <c>pedido.estorno-confirmado</c>.</summary>
public sealed record DadosEstornoConfirmado
{
    public required Guid PedidoId { get; init; }

    public required string Numero { get; init; }

    public required string Link { get; init; }
}

/// <summary>Serialização do corpo, no mesmo formato dos eventos (camelCase, enums como texto).</summary>
public static class SerializacaoNotificacoes
{
    public static string Serializar<TDados>(CorpoNotificacao<TDados> corpo) =>
        JsonSerializer.Serialize(corpo, SerializacaoEventos.Opcoes);

    /// <summary>Caminho da consulta do pedido, que confirma o aviso (FR-002).</summary>
    public static string Link(Guid pedidoId) => $"/api/v1/pedidos/{pedidoId}";
}
