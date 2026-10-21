using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Seguranca;

/// <summary>
/// Varre todas as rotas mapeadas e garante o Princípio III também para rotas futuras: só
/// register, login e a documentação (fora de produção) podem ser anônimas; todas as outras
/// declaram autorização explicitamente.
/// </summary>
public sealed class AutorizacaoEndpointsTestes(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    private static readonly string[] RotasAnonimasPermitidas = ["/api/v1/auth/register", "/api/v1/auth/login"];
    private static readonly string[] PrefixosDocumentacao = ["/openapi/", "/scalar"];

    [Fact]
    public void Toda_rota_exige_autorizacao_explicita_exceto_as_publicas_permitidas()
    {
        var rotas = fabrica.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();

        rotas.Should().NotBeEmpty();
        foreach (var rota in rotas)
        {
            var caminho = "/" + rota.RoutePattern.RawText!.TrimStart('/');
            var anonima = rota.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var publicaPermitida = RotasAnonimasPermitidas.Contains(caminho)
                || PrefixosDocumentacao.Any(p => caminho.StartsWith(p, StringComparison.Ordinal));

            if (anonima)
            {
                publicaPermitida.Should().BeTrue($"a rota {caminho} é anônima sem estar na lista de rotas públicas");
            }
            else
            {
                rota.Metadata.GetOrderedMetadata<IAuthorizeData>().Should()
                    .NotBeEmpty($"a rota {caminho} deve declarar uma policy de autorização");
            }
        }
    }
}
