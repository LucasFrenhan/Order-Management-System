using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>
/// Tokens opacos de renovação, recuperação e confirmação (REQ-24, R-01): 32 bytes aleatórios em
/// Base64 URL. Só o SHA-256 é gravado: 256 bits aleatórios não se adivinham por força bruta, então
/// um hash lento ou com sal não acrescentaria nada, e a busca fica por igualdade.
/// </summary>
public static class GeradorTokenOpaco
{
    public const int TamanhoBytes = 32;

    /// <summary>Base64 URL de 32 bytes, sem preenchimento.</summary>
    public const int TamanhoTexto = 43;

    /// <summary>O que os DTOs aceitam receber: limita o que é calculado, com folga para mudar a forma do token.</summary>
    public const int TamanhoMaximoRecebido = 128;

    public static string Gerar() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TamanhoBytes));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}
