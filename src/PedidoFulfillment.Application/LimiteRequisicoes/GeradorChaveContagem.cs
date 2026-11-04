using System.Security.Cryptography;
using System.Text;

namespace PedidoFulfillment.Application.LimiteRequisicoes;

/// <summary>
/// Forma não legível da identidade de uma contagem (FR-010, R-08): HMAC, porque um hash sem segredo
/// seria reversível (os IPv4 são poucos). O e-mail é comparado como no cadastro, sem espaços nas
/// pontas e sem diferenciar maiúsculas (FR-003).
/// </summary>
public static class GeradorChaveContagem
{
    public static byte[] Gerar(byte[] chave, string politica, string origem, string? email = null)
    {
        var identidade = $"{politica}\0{origem}\0{email?.Trim().ToUpperInvariant()}";
        return HMACSHA256.HashData(chave, Encoding.UTF8.GetBytes(identidade));
    }
}
