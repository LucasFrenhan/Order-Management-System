namespace PedidoFulfillment.Infrastructure.Persistencia.Configuracoes;

/// <summary>
/// Monta a condição SQL das check constraints de enum a partir dos nomes do próprio enum
/// (research R-05): um valor novo sem migration nova é acusado pelo teste de modelo sincronizado.
/// </summary>
internal static class RestricoesEnum
{
    public static string Em<TEnum>(string coluna) where TEnum : struct, Enum =>
        $"[{coluna}] IN ({string.Join(", ", Enum.GetNames<TEnum>().Select(n => $"N'{n}'"))})";
}
