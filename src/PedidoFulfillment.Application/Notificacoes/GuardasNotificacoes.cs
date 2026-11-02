using System.Runtime.CompilerServices;

namespace PedidoFulfillment.Application.Notificacoes;

/// <summary>
/// Guardas de argumento das entidades de notificação (REQ-18), com as mesmas regras das guardas
/// do Domain, que são internas a ele.
/// </summary>
internal static class GuardasNotificacoes
{
    public static DateTimeOffset Utc(DateTimeOffset instante, [CallerArgumentExpression(nameof(instante))] string? nome = null) =>
        instante.Offset == TimeSpan.Zero ? instante : throw new ArgumentException("O instante deve estar em UTC (deslocamento zero).", nome);

    /// <summary>Texto obrigatório, aparado, com até <paramref name="tamanhoMaximo"/> caracteres.</summary>
    public static string Texto(string? valor, int tamanhoMaximo, [CallerArgumentExpression(nameof(valor))] string? nome = null) =>
        string.IsNullOrWhiteSpace(valor) ? throw new ArgumentException("O valor é obrigatório.", nome) : Limitar(valor.Trim(), tamanhoMaximo, nome);

    /// <summary>Texto opcional, aparado; vazio vira nulo.</summary>
    public static string? TextoOpcional(string? valor, int tamanhoMaximo, [CallerArgumentExpression(nameof(valor))] string? nome = null) =>
        string.IsNullOrWhiteSpace(valor) ? null : Limitar(valor.Trim(), tamanhoMaximo, nome);

    public static Guid Identificador(Guid valor, [CallerArgumentExpression(nameof(valor))] string? nome = null) =>
        valor == Guid.Empty ? throw new ArgumentException("O identificador é obrigatório.", nome) : valor;

    private static string Limitar(string valor, int tamanhoMaximo, string? nome) =>
        valor.Length <= tamanhoMaximo ? valor : throw new ArgumentException($"O valor deve ter no máximo {tamanhoMaximo} caracteres.", nome);
}
