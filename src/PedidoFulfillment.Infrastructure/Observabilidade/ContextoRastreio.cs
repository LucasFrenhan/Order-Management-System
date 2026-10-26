using System.Diagnostics;
using System.Text;

namespace PedidoFulfillment.Infrastructure.Observabilidade;

/// <summary>
/// Contexto de rastreamento no formato W3C <c>traceparent</c> (REQ-16, data-model): o que o
/// interceptor grava no banco e o que o RabbitMQ.Client grava nos cabeçalhos. Inválido é
/// ignorado, sem erro (FR-013).
/// </summary>
internal static class ContextoRastreio
{
    /// <summary><c>00-</c>, 32 hex do trace, <c>-</c>, 16 hex do span, <c>-</c>, 2 hex das flags.</summary>
    public const int Tamanho = 55;

    /// <summary>Cabeçalho gravado pelo RabbitMQ.Client na publicação (propagador W3C padrão).</summary>
    public const string Cabecalho = "traceparent";

    /// <summary>O id W3C da activity corrente, ou nulo sem activity ou com formato hierárquico.</summary>
    public static string? Atual() =>
        Activity.Current is { IdFormat: ActivityIdFormat.W3C, Id: { Length: Tamanho } id } ? id : null;

    public static bool TentarLer(string? texto, out ActivityContext contexto)
    {
        contexto = default;
        // Só a versão 00, a única que o OMS grava; trace e span zerados são inválidos pelo W3C.
        if (texto is not { Length: Tamanho } || !texto.StartsWith("00-", StringComparison.Ordinal)
            || !ActivityContext.TryParse(texto, null, isRemote: true, out var lido)
            || lido.TraceId == default || lido.SpanId == default)
        {
            return false;
        }

        contexto = lido;
        return true;
    }

    /// <summary>O contexto do cabeçalho <c>traceparent</c> de uma mensagem (bytes UTF-8 ou texto).</summary>
    public static ActivityContext? DoCabecalho(IDictionary<string, object?>? cabecalhos)
    {
        if (cabecalhos is null || !cabecalhos.TryGetValue(Cabecalho, out var valor))
        {
            return null;
        }

        var texto = valor switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string cadeia => cadeia,
            _ => null
        };
        return TentarLer(texto, out var contexto) ? contexto : null;
    }
}
