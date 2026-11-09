using System.Net;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Autenticacao.CenarioAutenticacao;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>
/// Retirada de emergência da chave do JWT com SQL Server real (REQ-27, US2 cenário 4, FR-009, SC-003): o token de
/// acesso da chave retirada recebe 401, e a renovação da sessão, que não depende da chave, devolve um token da ativa.
/// </summary>
public sealed class RotacaoChaveSessoesTestes(BancoDadosTeste banco)
{
    private const string RotaPedidos = "/api/v1/pedidos";

    private static Dictionary<string, string?> SoAChave(string identificador, string valor) => new()
    {
        ["Jwt:Chaves:0:Identificador"] = identificador,
        ["Jwt:Chaves:0:Valor"] = valor,
        ["Jwt:ChaveAtiva"] = identificador
    };

    [Fact]
    public async Task Retirada_de_emergencia_recusa_o_acesso_e_a_renovacao_devolve_um_token_da_chave_nova()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        Tokens tokens;
        await using (var antes = Api(stringConexao, SoAChave("vazada", "valor-da-chave-vazada-com-mais-de-32-bytes")))
        {
            var cliente = antes.CreateClient();
            await RegistrarAsync(cliente);
            tokens = await LoginAsync(cliente);
            (await cliente.ComToken(tokens.Acesso).GetAsync(RotaPedidos, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await using var depois = Api(stringConexao, SoAChave("emergencia", "valor-da-chave-de-emergencia-com-mais-de-32-bytes"));
        var novoCliente = depois.CreateClient();

        (await novoCliente.ComToken(tokens.Acesso).GetAsync(RotaPedidos, Cancelamento)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "a chave que assinou o token foi retirada");

        var renovacao = await RenovarAsync(novoCliente, tokens.Renovacao);
        renovacao.StatusCode.Should().Be(HttpStatusCode.OK, "a renovação não depende da chave do JWT (FR-009)");
        var novos = await TokensAsync(renovacao);
        new JsonWebTokenHandler().ReadJsonWebToken(novos.Acesso).Kid.Should().Be("emergencia");
        (await novoCliente.ComToken(novos.Acesso).GetAsync(RotaPedidos, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
