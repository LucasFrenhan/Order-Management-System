namespace PedidoFulfillment.Application.Notificacoes;

/// <summary>Uma entrega para a rota de entregas do destino (REQ-18, FR-011), com o que vem do aviso.</summary>
public sealed record ResumoEntrega(
    Guid IdNotificacao,
    string Tipo,
    Guid PedidoId,
    SituacaoEntrega Situacao,
    int Tentativas,
    int? UltimoCodigoHttp,
    string? UltimoErro,
    DateTimeOffset CriadaEm,
    DateTimeOffset? ProximaTentativaEm,
    DateTimeOffset? FinalizadaEm);

/// <summary>Destinos e entregas no banco, para a API (REQ-18, research R-15).</summary>
public interface IArmazenamentoNotificacoes
{
    /// <summary>Os destinos de um Cliente, ou os globais com <paramref name="clienteId"/> nulo, por ordem de cadastro.</summary>
    Task<IReadOnlyList<DestinoNotificacao>> ListarDestinosAsync(Guid? clienteId, CancellationToken cancellationToken);

    /// <summary>O destino, só se for do escopo (do Cliente, ou global com <paramref name="clienteId"/> nulo).</summary>
    Task<DestinoNotificacao?> ObterDestinoAsync(Guid id, Guid? clienteId, CancellationToken cancellationToken);

    /// <summary>
    /// Executa <paramref name="acao"/> numa transação com lock de aplicação do dono, para a contagem
    /// do limite e o cadastro não correrem com outro cadastro do mesmo dono (FR-010).
    /// </summary>
    Task<T> ExecutarComBloqueioDoDonoAsync<T>(Guid? clienteId, Func<CancellationToken, Task<T>> acao, CancellationToken cancellationToken);

    Task<int> ContarDestinosAsync(Guid? clienteId, CancellationToken cancellationToken);

    void Adicionar(DestinoNotificacao destino);

    void Remover(DestinoNotificacao destino);

    /// <summary>Marca como descartadas as entregas pendentes do destino; devolve quantas.</summary>
    Task<int> DescartarPendentesAsync(Guid destinoId, DateTimeOffset instante, CancellationToken cancellationToken);

    /// <summary>As últimas entregas do destino, da mais nova para a mais antiga.</summary>
    Task<IReadOnlyList<ResumoEntrega>> ListarEntregasAsync(Guid destinoId, int limite, CancellationToken cancellationToken);

    Task SalvarAsync(CancellationToken cancellationToken);
}

/// <summary>Confere se o host de uma URL resolve só para endereços públicos (REQ-18, FR-008, research R-08).</summary>
public interface IVerificadorEndereco
{
    Task<bool> EnderecoPublicoAsync(Uri url, CancellationToken cancellationToken);
}

/// <summary>Cifra do segredo dos destinos com a chave da configuração (REQ-18, FR-009, research R-15).</summary>
public interface ICifraSegredos
{
    byte[] Cifrar(byte[] segredo);

    /// <exception cref="System.Security.Cryptography.CryptographicException">Conteúdo adulterado ou cifrado com outra chave.</exception>
    byte[] Decifrar(byte[] cifrado);
}
