namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>
/// Seção <c>Banco</c> (contracts/configuracao.md).
/// </summary>
public sealed class OpcoesBanco
{
    public const string Secao = "Banco";

    /// <summary>
    /// Ligado (padrão), a API prepara o banco ao iniciar e não sobe sem ele (FR-017, FR-018).
    /// Desligado, inicia sem consultar o banco, como no REQ-02.
    /// </summary>
    public bool PrepararNaInicializacao { get; set; } = true;
}
