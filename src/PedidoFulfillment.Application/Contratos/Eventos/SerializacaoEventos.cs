using System.Text.Json;
using System.Text.Json.Serialization;

namespace PedidoFulfillment.Application.Contratos.Eventos;

/// <summary>
/// Formato único dos eventos no broker, usado por quem publica, por quem consome e pelos testes
/// (REQ-06, FR-003): camelCase, enums como texto, obrigatórios e nulos verificados na leitura.
/// Campos desconhecidos são ignorados, para aceitar acréscimos compatíveis na mesma versão.
/// </summary>
public static class SerializacaoEventos
{
    public static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    public static string Serializar<T>(EnvelopeEvento<T> envelope) => JsonSerializer.Serialize(envelope, Opcoes);

    public static EnvelopeEvento<T> Desserializar<T>(string json) =>
        JsonSerializer.Deserialize<EnvelopeEvento<T>>(json, Opcoes)
        ?? throw new JsonException("Evento vazio.");
}
