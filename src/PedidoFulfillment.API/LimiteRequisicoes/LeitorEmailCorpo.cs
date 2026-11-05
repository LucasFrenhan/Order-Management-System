using System.Text.Json;

namespace PedidoFulfillment.API.LimiteRequisicoes;

/// <summary>
/// Lê o e-mail do corpo do login, da recuperação e do reenvio antes da rota, sem consumir o corpo (R-09). Qualquer corpo sem um
/// e-mail utilizável devolve nulo, e a tentativa conta só pela origem (FR-003). A rota segue com a
/// sua validação de hoje (FR-022).
/// </summary>
public static class LeitorEmailCorpo
{
    private const int TamanhoMaximoCorpo = 16 * 1024;
    private const int TamanhoMaximoEmail = 256;
    private const string CampoEmail = "email";

    public static async Task<string?> LerAsync(HttpRequest requisicao, CancellationToken cancellationToken)
    {
        requisicao.EnableBuffering();
        var memoria = new byte[TamanhoMaximoCorpo + 1];
        var lidos = 0;
        int ultimo;
        do
        {
            ultimo = await requisicao.Body.ReadAsync(memoria.AsMemory(lidos), cancellationToken);
            lidos += ultimo;
        }
        while (ultimo > 0 && lidos < memoria.Length);

        requisicao.Body.Position = 0;
        return lidos > TamanhoMaximoCorpo ? null : Extrair(memoria.AsMemory(0, lidos));
    }

    private static string? Extrair(ReadOnlyMemory<byte> corpo)
    {
        try
        {
            using var documento = JsonDocument.Parse(corpo);
            if (documento.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var propriedade in documento.RootElement.EnumerateObject())
            {
                if (propriedade.Name.Equals(CampoEmail, StringComparison.OrdinalIgnoreCase))
                {
                    return Utilizavel(propriedade.Value);
                }
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? Utilizavel(JsonElement valor)
    {
        if (valor.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var email = valor.GetString()?.Trim();
        return string.IsNullOrEmpty(email) || email.Length > TamanhoMaximoEmail ? null : email;
    }
}
