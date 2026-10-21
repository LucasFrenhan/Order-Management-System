using System.Net;
using FluentAssertions;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>
/// SC-002: a tabela de acesso do contrato v1 verificada com tokens emitidos pelo próprio login,
/// para cada perfil e cada rota protegida (FR-013).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class AutorizacaoComTokensReaisTestes(BancoDadosTeste banco)
{
    [Fact]
    public async Task Cada_rota_protegida_responde_conforme_o_perfil_do_token_do_login()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = UsuariosIniciaisComBancoTestes.Api(banco.NovoBanco());
        var anonimo = await LoginComBancoTestes.ClienteComContaAsync(api);
        var tokens = new Dictionary<PerfilUsuario, string>
        {
            [PerfilUsuario.Cliente] = await LoginComBancoTestes.TokenAsync(anonimo, "Maria@Exemplo.com.br", "SenhaForte1"),
            [PerfilUsuario.Admin] = await LoginComBancoTestes.TokenAsync(
                anonimo, UsuariosIniciaisComBancoTestes.EmailAdmin, UsuariosIniciaisComBancoTestes.SenhaAdmin),
            [PerfilUsuario.OperadorLogistica] = await LoginComBancoTestes.TokenAsync(
                anonimo, UsuariosIniciaisComBancoTestes.EmailOperador, UsuariosIniciaisComBancoTestes.SenhaOperador)
        };

        foreach (var rota in Exemplos.RotasProtegidas)
        {
            (await anonimo.SendAsync(rota.Requisicao(), TestContext.Current.CancellationToken)).StatusCode
                .Should().Be(HttpStatusCode.Unauthorized, $"{rota.Nome} sem token");

            foreach (var (perfil, token) in tokens)
            {
                var resposta = await api.CreateClient().ComToken(token).SendAsync(rota.Requisicao(), TestContext.Current.CancellationToken);

                // Autorizado chega ao serviço provisório de pedidos (501 até o REQ-05).
                var esperado = rota.Autorizados.Contains(perfil) ? HttpStatusCode.NotImplemented : HttpStatusCode.Forbidden;
                resposta.StatusCode.Should().Be(esperado, $"{rota.Nome} com token de {perfil}");
            }
        }
    }
}
