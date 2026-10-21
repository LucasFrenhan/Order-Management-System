using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>
/// Token emitido pela API (contracts/token-acesso.md), sem banco: o emissor é resolvido da API.
/// </summary>
public sealed class EmissorTokenTestes(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Token_tem_exatamente_os_claims_do_contrato()
    {
        var instante = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
        await using var api = fabrica.WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton<TimeProvider>(new RelogioFixo(instante))));
        var usuarioId = Guid.NewGuid();

        var login = api.Services.GetRequiredService<IEmissorToken>().Emitir(usuarioId, [PerfilUsuario.Cliente, PerfilUsuario.Admin]);

        var token = new JsonWebTokenHandler().ReadJsonWebToken(login.TokenAcesso);
        token.Alg.Should().Be("HS256");
        token.Subject.Should().Be(usuarioId.ToString());
        token.Id.Should().NotBeNullOrEmpty();
        token.Issuer.Should().Be(FabricaApi.EmissorTeste);
        token.Audiences.Should().Equal(FabricaApi.AudienciaTeste);
        token.GetClaim("iat").Value.Should().Be(instante.ToUnixTimeSeconds().ToString());
        token.Claims.Where(c => c.Type == "role").Select(c => c.Value).Should().BeEquivalentTo("Cliente", "Admin");
        token.Claims.Select(c => c.Type).Distinct().Should().BeEquivalentTo("sub", "jti", "role", "iss", "aud", "iat", "nbf", "exp");
        token.ValidTo.Should().Be(instante.AddMinutes(60).UtcDateTime);
        login.ExpiraEm.Should().Be(instante.AddMinutes(60));
        login.TipoToken.Should().Be("Bearer");
        login.Perfis.Should().Equal(PerfilUsuario.Cliente, PerfilUsuario.Admin);
    }

    [Theory]
    [InlineData(PerfilUsuario.Cliente, HttpStatusCode.NotImplemented)]
    [InlineData(PerfilUsuario.Admin, HttpStatusCode.Forbidden)]
    public async Task Token_emitido_e_aceito_pelas_rotas_protegidas_conforme_o_perfil(PerfilUsuario perfil, HttpStatusCode esperado)
    {
        var token = fabrica.Services.GetRequiredService<IEmissorToken>().Emitir(Guid.NewGuid(), [perfil]).TokenAcesso;

        var resposta = await fabrica.CreateClient().ComToken(token).SendAsync(Exemplos.CriarPedido(), Cancelamento);

        // 501: passou pela autorização e chegou ao serviço provisório de pedidos (REQ-05).
        resposta.StatusCode.Should().Be(esperado);
    }

    [Fact]
    public async Task Token_sem_nenhum_perfil_recebe_403_em_todas_as_rotas_protegidas()
    {
        var token = fabrica.Services.GetRequiredService<IEmissorToken>().Emitir(Guid.NewGuid(), []).TokenAcesso;
        var cliente = fabrica.CreateClient().ComToken(token);

        foreach (var rota in Exemplos.RotasProtegidas)
        {
            var resposta = await cliente.SendAsync(rota.Requisicao(), Cancelamento);
            resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden, rota.Nome);
        }
    }

    private sealed class RelogioFixo(DateTimeOffset instante) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => instante;
    }
}
