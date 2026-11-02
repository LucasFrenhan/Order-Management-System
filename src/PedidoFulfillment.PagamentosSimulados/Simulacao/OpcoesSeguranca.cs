using Microsoft.Extensions.Options;

namespace PedidoFulfillment.PagamentosSimulados.Simulacao;

/// <summary>
/// Seção <c>Seguranca</c> (REQ-19, research R-05): a chave do OMS (<c>X-Api-Key</c>) e a chave de
/// controle (<c>X-Chave-Controle</c>). Nenhuma das duas aparece em log.
/// </summary>
public sealed class OpcoesSeguranca
{
    public const string Secao = "Seguranca";

    public const int TamanhoMinimo = 16;

    public string ChaveApi { get; set; } = "";

    public string ChaveControle { get; set; } = "";
}

/// <summary>Obrigatórias, com pelo menos 16 caracteres e diferentes entre si; o erro nomeia a chave, nunca o valor.</summary>
public sealed class ValidadorOpcoesSeguranca : IValidateOptions<OpcoesSeguranca>
{
    public ValidateOptionsResult Validate(string? name, OpcoesSeguranca options)
    {
        var erros = new List<string>();
        Exigir(erros, nameof(OpcoesSeguranca.ChaveApi), options.ChaveApi);
        Exigir(erros, nameof(OpcoesSeguranca.ChaveControle), options.ChaveControle);
        if (erros.Count == 0 && string.Equals(options.ChaveApi, options.ChaveControle, StringComparison.Ordinal))
        {
            erros.Add($"{OpcoesSeguranca.Secao}:{nameof(OpcoesSeguranca.ChaveControle)} deve ser diferente de {OpcoesSeguranca.Secao}:{nameof(OpcoesSeguranca.ChaveApi)}.");
        }

        return erros.Count > 0 ? ValidateOptionsResult.Fail(erros) : ValidateOptionsResult.Success;
    }

    private static void Exigir(List<string> erros, string chave, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            erros.Add($"{OpcoesSeguranca.Secao}:{chave} é obrigatória.");
        }
        else if (valor.Length < OpcoesSeguranca.TamanhoMinimo)
        {
            erros.Add($"{OpcoesSeguranca.Secao}:{chave} deve ter pelo menos {OpcoesSeguranca.TamanhoMinimo} caracteres.");
        }
    }
}
