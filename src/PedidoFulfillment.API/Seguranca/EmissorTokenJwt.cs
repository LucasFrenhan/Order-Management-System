using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.API.Seguranca;

/// <summary>
/// Emite o JWT do login com as mesmas opções e claims que <see cref="ConfiguracaoJwt"/> valida
/// (research R-06, contracts/token-acesso.md). Não leva nome, e-mail nem outro dado pessoal.
/// </summary>
public sealed class EmissorTokenJwt(IOptions<OpcoesJwt> opcoes, TimeProvider relogio) : IEmissorToken
{
    private readonly JsonWebTokenHandler manipulador = new();

    public LoginResponseDTO Emitir(Guid usuarioId, IReadOnlyList<PerfilUsuario> perfis)
    {
        var jwt = opcoes.Value;
        var emissao = relogio.GetUtcNow();
        var expiracao = emissao.AddMinutes(jwt.DuracaoTokenMinutos);
        var claims = new Dictionary<string, object>
        {
            [ConfiguracaoJwt.ClaimUsuario] = usuarioId.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString()
        };
        if (perfis.Count > 0)
        {
            claims[ConfiguracaoJwt.ClaimPerfil] = perfis.Select(p => p.ToString()).ToArray();
        }

        var token = manipulador.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Emissor,
            Audience = jwt.Audiencia,
            Claims = claims,
            // Todos os instantes vêm do mesmo relógio, para que expiraEm e exp coincidam.
            IssuedAt = emissao.UtcDateTime,
            NotBefore = emissao.UtcDateTime,
            Expires = expiracao.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.ChaveAssinatura)), SecurityAlgorithms.HmacSha256)
        });

        return new LoginResponseDTO { TokenAcesso = token, ExpiraEm = expiracao, Perfis = perfis };
    }
}
