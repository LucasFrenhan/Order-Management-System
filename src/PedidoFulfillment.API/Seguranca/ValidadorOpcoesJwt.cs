using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace PedidoFulfillment.API.Seguranca;

/// <summary>
/// Regras da configuração das chaves do JWT, conferidas na partida (REQ-27, FR-005 e FR-007; research R-04).
/// Junta todas as falhas. As mensagens citam o identificador da chave, que é público, ou a posição na lista
/// quando o identificador está fora do formato (pode ser um valor colado no campo errado); nunca o valor.
/// </summary>
public sealed partial class ValidadorOpcoesJwt : IValidateOptions<OpcoesJwt>
{
    public const int MaximoChaves = 3;
    public const int MinimoBytesChave = 32;

    public static bool IdentificadorValido(string? identificador) =>
        identificador is not null && FormatoIdentificador().IsMatch(identificador);

    public ValidateOptionsResult Validate(string? name, OpcoesJwt options)
    {
        var falhas = new List<string>();
        if (!string.IsNullOrEmpty(options.ChaveAssinatura))
        {
            falhas.Add("Jwt:ChaveAssinatura não é mais aceita. Configure Jwt:Chaves e Jwt:ChaveAtiva (docs/rotacao-chave-jwt.md).");
        }

        if (string.IsNullOrEmpty(options.Emissor) || string.IsNullOrEmpty(options.Audiencia))
        {
            falhas.Add("Jwt:Emissor e Jwt:Audiencia são obrigatórios.");
        }

        ValidarChaves(options.Chaves, falhas);
        ValidarChaveAtiva(options, falhas);

        return falhas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falhas);
    }

    private static void ValidarChaves(IReadOnlyList<ChaveJwtConfigurada> chaves, List<string> falhas)
    {
        if (chaves.Count == 0)
        {
            falhas.Add("Jwt:Chaves deve ter ao menos uma chave.");
            return;
        }

        if (chaves.Count > MaximoChaves)
        {
            falhas.Add($"Jwt:Chaves aceita no máximo {MaximoChaves} chaves.");
        }

        var vistos = new HashSet<string>(StringComparer.Ordinal);
        var repetidos = new HashSet<string>(StringComparer.Ordinal);
        for (var posicao = 0; posicao < chaves.Count; posicao++)
        {
            var identificador = chaves[posicao].Identificador;
            var valido = IdentificadorValido(identificador);
            if (!valido)
            {
                falhas.Add($"Jwt:Chaves:{posicao}:Identificador deve ter de 1 a 64 caracteres entre letras sem acento, dígitos, '-', '_' e '.'.");
            }
            else if (!vistos.Add(identificador) && repetidos.Add(identificador))
            {
                falhas.Add($"Jwt:Chaves: o identificador '{identificador}' está repetido.");
            }

            if (Encoding.UTF8.GetByteCount(chaves[posicao].Valor ?? "") < MinimoBytesChave)
            {
                falhas.Add(valido
                    ? $"Jwt:Chaves: a chave '{identificador}' deve ter ao menos {MinimoBytesChave} bytes."
                    : $"Jwt:Chaves:{posicao}:Valor deve ter ao menos {MinimoBytesChave} bytes.");
            }
        }
    }

    private static void ValidarChaveAtiva(OpcoesJwt options, List<string> falhas)
    {
        if (string.IsNullOrEmpty(options.ChaveAtiva))
        {
            // A falta de chaves já foi apontada; a ativa só faz sentido com o formato novo em uso.
            falhas.Add("Jwt:ChaveAtiva é obrigatória.");
            return;
        }

        if (options.Chaves.Any(c => string.Equals(c.Identificador, options.ChaveAtiva, StringComparison.Ordinal)))
        {
            return;
        }

        falhas.Add(IdentificadorValido(options.ChaveAtiva)
            ? $"Jwt:ChaveAtiva '{options.ChaveAtiva}' não está entre as chaves de Jwt:Chaves."
            : "Jwt:ChaveAtiva deve ser o identificador de uma das chaves de Jwt:Chaves.");
    }

    // \z, e não $, que aceitaria uma quebra de linha no fim.
    [GeneratedRegex(@"^[A-Za-z0-9._-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex FormatoIdentificador();
}
