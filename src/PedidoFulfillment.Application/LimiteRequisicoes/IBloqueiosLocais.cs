namespace PedidoFulfillment.Application.LimiteRequisicoes;

/// <summary>
/// Recusas já conhecidas por esta instância, válidas até o fim da janela (R-05). Evitam ir ao
/// armazenamento em uma inundação da mesma origem. Como a janela é fixa, uma recusa conhecida
/// continua certa até o prazo, e o estado único não muda (FR-006).
/// </summary>
public interface IBloqueiosLocais
{
    void Registrar(byte[] chave, TimeSpan espera);

    bool TentarObter(byte[] chave, out TimeSpan espera);
}
