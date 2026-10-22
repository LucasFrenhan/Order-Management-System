using PedidoFulfillment.Application.Contratos.Eventos;

namespace PedidoFulfillment.Application.Mensageria;

/// <summary>
/// Evento pendente do outbox (REQ-06, R-04): gravado na mesma operação que o originou e
/// publicado depois pelo Worker, só marcado como enviado após a confirmação do broker.
/// </summary>
public sealed class EventoSaida
{
    public const int TetoEsperaSegundos = 10;
    public const int TamanhoMaximoTipo = 100;
    public const int TamanhoMaximoErro = 500;

    private EventoSaida()
    {
        Tipo = "";
        Conteudo = "";
    }

    /// <summary>Ordem de registro, que é também a ordem de publicação.</summary>
    public long Id { get; private set; }

    public Guid IdEvento { get; private set; }

    public string Tipo { get; private set; }

    /// <summary>Envelope serializado (<see cref="SerializacaoEventos"/>).</summary>
    public string Conteudo { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public DateTimeOffset? EnviadoEm { get; private set; }

    public int Tentativas { get; private set; }

    public DateTimeOffset ProximaTentativaEm { get; private set; }

    public string? UltimoErro { get; private set; }

    public static EventoSaida De<T>(EnvelopeEvento<T> envelope, DateTimeOffset instante) => new()
    {
        IdEvento = envelope.IdEvento,
        Tipo = envelope.Tipo,
        Conteudo = SerializacaoEventos.Serializar(envelope),
        CriadoEm = instante,
        ProximaTentativaEm = instante
    };

    public void MarcarEnviado(DateTimeOffset instante) => EnviadoEm = instante;

    /// <summary>Espera 2, 4, 8... segundos até o teto, para não sobrecarregar o broker ao voltar (FR-007).</summary>
    public void RegistrarFalha(string erro, DateTimeOffset instante)
    {
        Tentativas++;
        UltimoErro = erro.Length > TamanhoMaximoErro ? erro[..TamanhoMaximoErro] : erro;
        ProximaTentativaEm = instante.AddSeconds(Math.Min(Math.Pow(2, Tentativas), TetoEsperaSegundos));
    }
}
