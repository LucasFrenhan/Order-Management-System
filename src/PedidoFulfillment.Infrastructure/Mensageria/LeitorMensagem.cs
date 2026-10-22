using System.Text.Json;
using PedidoFulfillment.Application.Contratos.Eventos;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>Resultado da leitura: o envelope, ou o motivo de a mensagem ser irrecuperável.</summary>
internal sealed record ResultadoLeitura<TDados>(EnvelopeEvento<TDados>? Envelope, string? Motivo)
{
    public static ResultadoLeitura<TDados> Valida(EnvelopeEvento<TDados> envelope) => new(envelope, null);

    public static ResultadoLeitura<TDados> Irrecuperavel(string motivo) => new(null, motivo);
}

/// <summary>
/// Lê uma mensagem do broker (REQ-06, R-03). Uma mensagem ilegível nunca vai dar certo, então
/// não adianta tentar de novo: vai direto para a DLQ (FR-014). A validação é estrutural, pelo
/// próprio System.Text.Json; a conformidade completa com o schema é verificada nos testes.
/// </summary>
internal static class LeitorMensagem
{
    public static ResultadoLeitura<TDados> Ler<TDados>(ReadOnlyMemory<byte> corpo, IReadOnlyCollection<string> tiposAceitos)
    {
        if (corpo.IsEmpty)
        {
            return ResultadoLeitura<TDados>.Irrecuperavel("Corpo vazio.");
        }

        string? tipo;
        int versao;
        try
        {
            using var documento = JsonDocument.Parse(corpo);
            var raiz = documento.RootElement;
            tipo = raiz.TryGetProperty("tipo", out var elementoTipo) && elementoTipo.ValueKind == JsonValueKind.String ? elementoTipo.GetString() : null;
            versao = raiz.TryGetProperty("versao", out var elementoVersao) && elementoVersao.TryGetInt32(out var lida) ? lida : 0;
        }
        catch (JsonException excecao)
        {
            return ResultadoLeitura<TDados>.Irrecuperavel($"JSON inválido: {excecao.Message}");
        }

        if (tipo is null)
        {
            return ResultadoLeitura<TDados>.Irrecuperavel("Envelope sem tipo.");
        }

        if (!TiposEvento.TipoDados.TryGetValue(tipo, out var tipoDados))
        {
            return ResultadoLeitura<TDados>.Irrecuperavel($"Tipo desconhecido: {tipo}.");
        }

        if (versao != TiposEvento.VersaoAtual)
        {
            return ResultadoLeitura<TDados>.Irrecuperavel($"Versão não suportada: {versao}.");
        }

        if (!tiposAceitos.Contains(tipo) || tipoDados != typeof(TDados))
        {
            return ResultadoLeitura<TDados>.Irrecuperavel($"Tipo não consumido por este consumidor: {tipo}.");
        }

        try
        {
            var envelope = JsonSerializer.Deserialize<EnvelopeEvento<TDados>>(corpo.Span, SerializacaoEventos.Opcoes);
            return envelope is null
                ? ResultadoLeitura<TDados>.Irrecuperavel("Conteúdo fora do contrato: envelope nulo.")
                : ResultadoLeitura<TDados>.Valida(envelope);
        }
        catch (JsonException excecao)
        {
            return ResultadoLeitura<TDados>.Irrecuperavel($"Conteúdo fora do contrato: {excecao.Message}");
        }
    }
}
