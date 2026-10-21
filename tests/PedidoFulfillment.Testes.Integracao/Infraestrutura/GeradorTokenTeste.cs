using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Gera tokens JWT com a chave de teste da <see cref="FabricaApi"/>, inclusive inválidos.
/// </summary>
public static class GeradorTokenTeste
{
    public static string Gerar(
        Guid? usuarioId = null,
        IEnumerable<PerfilUsuario>? perfis = null,
        bool expirado = false,
        bool assinaturaInvalida = false,
        string? audiencia = null)
    {
        var agora = DateTime.UtcNow;
        var claims = new Dictionary<string, object> { ["sub"] = (usuarioId ?? Guid.NewGuid()).ToString() };
        var nomesPerfis = (perfis ?? []).Select(p => p.ToString()).ToArray();
        if (nomesPerfis.Length > 0)
        {
            claims["role"] = nomesPerfis;
        }

        var chave = assinaturaInvalida ? "outra-chave-qualquer-com-mais-de-32-bytes!!" : FabricaApi.ChaveTeste;
        var descritor = new SecurityTokenDescriptor
        {
            Issuer = FabricaApi.EmissorTeste,
            Audience = audiencia ?? FabricaApi.AudienciaTeste,
            Claims = claims,
            IssuedAt = expirado ? agora.AddHours(-2) : agora,
            NotBefore = expirado ? agora.AddHours(-2) : agora.AddMinutes(-1),
            Expires = expirado ? agora.AddHours(-1) : agora.AddMinutes(60),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave)), SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descritor);
    }
}
