using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Gera tokens JWT com a chave de teste da <see cref="FabricaApi"/>, inclusive inválidos. Com
/// <paramref name="chave"/> e <paramref name="identificadorChave"/>, assina com outra chave e outro <c>kid</c>;
/// com <c>identificadorChave: null</c>, o token sai sem <c>kid</c> (REQ-27).
/// </summary>
public static class GeradorTokenTeste
{
    public static string Gerar(
        Guid? usuarioId = null,
        IEnumerable<PerfilUsuario>? perfis = null,
        bool expirado = false,
        bool assinaturaInvalida = false,
        string? audiencia = null,
        TimeSpan? validade = null,
        string? chave = null,
        string? identificadorChave = FabricaApi.IdentificadorChaveTeste)
    {
        var agora = DateTime.UtcNow;
        var claims = new Dictionary<string, object> { ["sub"] = (usuarioId ?? Guid.NewGuid()).ToString() };
        var nomesPerfis = (perfis ?? []).Select(p => p.ToString()).ToArray();
        if (nomesPerfis.Length > 0)
        {
            claims["role"] = nomesPerfis;
        }

        var valorChave = assinaturaInvalida ? "outra-chave-qualquer-com-mais-de-32-bytes!!" : chave ?? FabricaApi.ChaveTeste;
        var chaveAssinatura = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(valorChave)) { KeyId = identificadorChave };
        var descritor = new SecurityTokenDescriptor
        {
            Issuer = FabricaApi.EmissorTeste,
            Audience = audiencia ?? FabricaApi.AudienciaTeste,
            Claims = claims,
            IssuedAt = expirado ? agora.AddHours(-2) : agora,
            NotBefore = expirado ? agora.AddHours(-2) : agora.AddMinutes(-1),
            Expires = expirado ? agora.AddHours(-1) : agora + (validade ?? TimeSpan.FromMinutes(60)),
            SigningCredentials = new SigningCredentials(chaveAssinatura, SecurityAlgorithms.HmacSha256)
        };
        return new JsonWebTokenHandler().CreateToken(descritor);
    }
}
