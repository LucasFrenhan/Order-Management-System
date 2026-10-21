using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Serializacao;

/// <summary>
/// DTOs de autenticação serializados com as opções JSON reais da API (FR-008, FR-015).
/// </summary>
public sealed class SerializacaoAutenticacaoTestes(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    [Fact]
    public void Usuario_serializa_em_camel_case_com_perfis_como_texto_e_sem_senha()
    {
        var usuario = new UsuarioResponseDTO
        {
            Id = Guid.Parse("3f2b8c1e-6a4d-4e2f-9b1a-0c7d5e8f9a10"),
            Nome = "Maria Silva",
            Email = "maria.silva@exemplo.com.br",
            Perfis = [PerfilUsuario.Cliente]
        };

        var json = Serializar(usuario);

        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "nome", "email", "perfis");
        json.GetProperty("perfis")[0].GetString().Should().Be("Cliente");
        json.GetRawText().Should().NotContainEquivalentOf("senha").And.NotContainEquivalentOf("hash");
    }

    [Fact]
    public void Login_serializa_token_bearer_e_expiracao_iso_8601_utc()
    {
        var login = new LoginResponseDTO
        {
            TokenAcesso = "eyJ...",
            ExpiraEm = new DateTimeOffset(2026, 10, 21, 18, 0, 0, TimeSpan.Zero),
            Perfis = [PerfilUsuario.Cliente, PerfilUsuario.Admin]
        };

        var json = Serializar(login);

        json.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("tokenAcesso", "tipoToken", "expiraEm", "perfis");
        json.GetProperty("tipoToken").GetString().Should().Be("Bearer");
        json.GetProperty("expiraEm").GetString().Should().Be("2026-10-21T18:00:00+00:00");
        json.GetProperty("perfis").EnumerateArray().Select(p => p.GetString()).Should().Equal("Cliente", "Admin");
    }

    private JsonElement Serializar<T>(T valor) =>
        JsonSerializer.SerializeToElement(valor, OpcoesJson.DaApi(fabrica));
}
