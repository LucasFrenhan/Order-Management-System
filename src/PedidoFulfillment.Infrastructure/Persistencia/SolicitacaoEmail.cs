using PedidoFulfillment.Application.Emails;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// Um pedido de envio, a fila do Worker (REQ-24, R-03). Não guarda token nem corpo, e é apagado ao
/// ser processado: um e-mail sem conta fica no banco só até a rodada seguinte.
/// </summary>
internal sealed class SolicitacaoEmail
{
    public const int TamanhoDestino = 256;

    public Guid Id { get; init; }

    public TipoEmail Tipo { get; init; }

    /// <summary>Como foi informado, aparado. Serve só para achar a conta: o e-mail vai ao endereço guardado nela.</summary>
    public required string Destino { get; init; }

    public DateTimeOffset CriadaEm { get; init; }

    public DateTimeOffset ProximaTentativaEm { get; set; }

    public int Tentativas { get; set; }
}
