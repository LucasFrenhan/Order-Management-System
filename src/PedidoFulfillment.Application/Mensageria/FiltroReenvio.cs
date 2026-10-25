namespace PedidoFulfillment.Application.Mensageria;

/// <summary>
/// Quais mensagens da DLQ um pedido de reenvio seleciona (REQ-14, R-05). Campos nulos não
/// filtram; os informados se combinam.
/// </summary>
public sealed record FiltroReenvio(string? TipoEvento, DateTimeOffset? FalhouDe, DateTimeOffset? FalhouAte, string? TrechoMotivo)
{
    /// <summary>
    /// Tipo por igualdade ordinal; período pelo instante em que a mensagem foi para a DLQ, com as
    /// pontas incluídas; motivo por trecho, sem diferenciar maiúsculas. Sem o instante ou sem o
    /// motivo, a mensagem não atende a um filtro que os peça.
    /// </summary>
    public bool Atende(string? tipo, DateTimeOffset? falhouEm, string? motivo)
    {
        if (TipoEvento is not null && !string.Equals(tipo, TipoEvento, StringComparison.Ordinal))
        {
            return false;
        }

        if ((FalhouDe is not null || FalhouAte is not null)
            && (falhouEm is not { } instante || instante < FalhouDe || instante > FalhouAte))
        {
            return false;
        }

        return TrechoMotivo is null || (motivo is not null && motivo.Contains(TrechoMotivo, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Forma curta para o log: só os campos informados.</summary>
    public override string ToString()
    {
        var partes = new List<string>();
        if (TipoEvento is not null)
        {
            partes.Add($"tipo={TipoEvento}");
        }

        if (FalhouDe is not null || FalhouAte is not null)
        {
            partes.Add($"período={FalhouDe:O}..{FalhouAte:O}");
        }

        if (TrechoMotivo is not null)
        {
            partes.Add($"motivo~{TrechoMotivo}");
        }

        return partes.Count == 0 ? "nenhum" : string.Join(", ", partes);
    }
}
