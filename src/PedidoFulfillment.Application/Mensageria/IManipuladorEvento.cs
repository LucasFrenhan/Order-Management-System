using PedidoFulfillment.Application.Contratos.Eventos;

namespace PedidoFulfillment.Application.Mensageria;

/// <summary>
/// Consumidor de um evento (REQ-06, R-07; <c>contracts/topologia-e-consumo.md</c>). A base de
/// consumo abre o escopo e a transação: o efeito deve ser gravado pelas portas do Application
/// resolvidas nesse escopo, e a base faz o commit junto com o registro de processado. Para
/// publicar um evento, grave um <see cref="EventoSaida"/>; nunca publique direto no broker.
/// Uma exceção faz a mensagem ser tentada de novo e, esgotadas as tentativas, ir para a DLQ.
/// </summary>
public interface IManipuladorEvento<TDados>
{
    /// <summary>Nome do consumidor, que vira o nome da fila (<c>oms.{Consumidor}</c>), em kebab-case.</summary>
    string Consumidor { get; }

    /// <summary>Tipos de evento consumidos (chaves de roteamento).</summary>
    IReadOnlyCollection<string> Tipos { get; }

    Task ManipularAsync(EnvelopeEvento<TDados> evento, CancellationToken cancellationToken);
}
