namespace PedidoFulfillment.Application.Pedidos;

/// <summary>
/// Chave de idempotência de uma criação bem-sucedida (research R-03 do REQ-05): o par
/// (cliente, chave), a impressão do conteúdo e a resposta enviada, devolvida igual na repetição.
/// </summary>
public sealed class RegistroIdempotencia
{
    public RegistroIdempotencia(Guid clienteId, Guid chave, string impressao, Guid pedidoId, string resposta, DateTimeOffset criadoEm)
    {
        ClienteId = clienteId;
        Chave = chave;
        Impressao = impressao;
        PedidoId = pedidoId;
        Resposta = resposta;
        CriadoEm = criadoEm;
    }

    private RegistroIdempotencia()
    {
        Impressao = "";
        Resposta = "";
    }

    public Guid ClienteId { get; private set; }

    public Guid Chave { get; private set; }

    /// <summary>SHA-256 em hexadecimal do conteúdo normalizado (<see cref="ImpressaoConteudo"/>).</summary>
    public string Impressao { get; private set; }

    public Guid PedidoId { get; private set; }

    /// <summary>JSON do <c>PedidoResponseDTO</c> enviado na criação.</summary>
    public string Resposta { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }
}
