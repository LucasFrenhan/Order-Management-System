using System.Runtime.CompilerServices;

namespace PedidoFulfillment.Domain.Comum;

/// <summary>
/// Guardas de argumento das entidades (research R-02). Recusam dados que deixariam a entidade
/// inconsistente; não são erros de negócio do contrato.
/// </summary>
internal static class Guardas
{
    public static DateTimeOffset Utc(DateTimeOffset instante, [CallerArgumentExpression(nameof(instante))] string? nome = null)
    {
        if (instante.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("O instante deve estar em UTC (deslocamento zero).", nome);
        }

        return instante;
    }

    /// <summary>Texto obrigatório, aparado, com até <paramref name="tamanhoMaximo"/> caracteres.</summary>
    public static string Texto(string? valor, int tamanhoMaximo, [CallerArgumentExpression(nameof(valor))] string? nome = null)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new ArgumentException("O valor é obrigatório.", nome);
        }

        return Limitar(valor.Trim(), tamanhoMaximo, nome);
    }

    /// <summary>Texto opcional, aparado; vazio vira nulo.</summary>
    public static string? TextoOpcional(string? valor, int tamanhoMaximo, [CallerArgumentExpression(nameof(valor))] string? nome = null) =>
        string.IsNullOrWhiteSpace(valor) ? null : Limitar(valor.Trim(), tamanhoMaximo, nome);

    /// <summary>Valor monetário não negativo, arredondado para 2 casas (R-06).</summary>
    public static decimal Dinheiro(decimal valor, [CallerArgumentExpression(nameof(valor))] string? nome = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(valor, nome);
        return Math.Round(valor, 2, MidpointRounding.ToEven);
    }

    public static Guid Identificador(Guid valor, [CallerArgumentExpression(nameof(valor))] string? nome = null)
    {
        if (valor == Guid.Empty)
        {
            throw new ArgumentException("O identificador é obrigatório.", nome);
        }

        return valor;
    }

    private static string Limitar(string valor, int tamanhoMaximo, string? nome)
    {
        if (valor.Length > tamanhoMaximo)
        {
            throw new ArgumentException($"O valor deve ter no máximo {tamanhoMaximo} caracteres.", nome);
        }

        return valor;
    }
}
