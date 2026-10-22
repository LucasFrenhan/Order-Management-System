using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>
/// User Story 1 com SQL Server real, tokens do login e catálogo inicial (FR-001 a FR-006).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed partial class CriacaoPedidoComBancoTestes(BancoDadosTeste banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"^PED-\d{8}-\d{6}$")]
    private static partial Regex PadraoNumero();

    [Fact]
    public async Task Cliente_cria_o_pedido_com_precos_do_catalogo()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, token) = await ClienteAsync(api);

        var resposta = await CriarAsync(cliente, Guid.NewGuid(), (Cat001, 2), (Cat002, 1));

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        var corpo = await CorpoAsync(resposta);
        var id = corpo.GetProperty("id").GetGuid();
        resposta.Headers.Location!.ToString().Should().Be($"/api/v1/pedidos/{id}");
        corpo.GetProperty("status").GetString().Should().Be("Pendente");
        corpo.GetProperty("valorTotal").GetDecimal().Should().Be(829.70m);
        corpo.GetProperty("moeda").GetString().Should().Be("BRL");
        corpo.GetProperty("clienteId").GetGuid().Should().Be(Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(token).Subject));
        var numero = corpo.GetProperty("numero").GetString()!;
        numero.Should().MatchRegex(PadraoNumero());
        numero.Should().StartWith($"PED-{DateTime.UtcNow:yyyyMMdd}-");

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM ItensPedido WHERE PedidoId = '{id}'")).Should().Be(2);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM HistoricosStatusPedido WHERE PedidoId = '{id}'")).Should().Be(1);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM ChavesIdempotencia")).Should().Be(1);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT QuantidadeEstoque FROM Produtos WHERE Sku = 'CAT-001'")).Should().Be(50);
    }

    [Theory]
    [InlineData(9, 1)]
    [InlineData(10, 1)]
    [InlineData(8, 2)]
    public async Task Produto_sem_estoque_inativo_ou_insuficiente_responde_422(int catalogo, int quantidade)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);

        var resposta = await CriarAsync(cliente, Guid.NewGuid(), (Catalogo(catalogo), quantidade));

        resposta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await CodigoAsync(resposta)).Should().Be("PRODUTO_INDISPONIVEL");
        await NadaGravadoAsync(stringConexao);
    }

    [Fact]
    public async Task Produto_inexistente_responde_422_citando_todos_os_produtos()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var inexistente = Guid.CreateVersion7();

        var resposta = await CriarAsync(cliente, Guid.NewGuid(), (inexistente, 1), (Cat009, 1));

        resposta.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var corpo = await CorpoAsync(resposta);
        corpo.GetProperty("codigo").GetString().Should().Be("PRODUTO_INEXISTENTE");
        corpo.GetProperty("detail").GetString().Should().Contain(inexistente.ToString()).And.Contain(Cat009.ToString());
        await NadaGravadoAsync(stringConexao);
    }

    [Fact]
    public async Task Admin_recebe_403_e_requisicao_sem_chave_recebe_400()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        var (cliente, _, _) = await ClienteAsync(api);

        (await CriarAsync(admin, Guid.NewGuid(), (Cat001, 1))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var semChave = await cliente.PostAsJsonAsync(Rota, new { itens = new[] { new { produtoId = Cat001, quantidade = 1 } } }, Cancelamento);
        semChave.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await NadaGravadoAsync(stringConexao);
    }

    [Fact]
    public async Task Token_de_usuario_inexistente_recebe_500_generico_sem_gravar()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var token = api.Services.GetRequiredService<IEmissorToken>().Emitir(Guid.CreateVersion7(), [PerfilUsuario.Cliente]).TokenAcesso;

        var resposta = await CriarAsync(api.CreateClient().ComToken(token), Guid.NewGuid(), (Cat001, 1));

        resposta.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var corpo = await resposta.Content.ReadAsStringAsync(Cancelamento);
        corpo.Should().NotContainAny("FK_", "Usuarios", "SqlException", "INSERT");
        await NadaGravadoAsync(stringConexao);
    }

    [Fact]
    public async Task Logs_registram_criacao_e_status_sem_dados_sensiveis()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var logs = new CapturaLogs();
        await using var api = Api(banco.NovoBanco(), logs);
        var (cliente, email, token) = await ClienteAsync(api);
        var id = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await MudarStatusAsync(cliente, id, "Cancelado", "Desisti")).StatusCode.Should().Be(HttpStatusCode.OK);

        logs.Registros.Select(r => r.Evento.Name).Should().Contain(["PedidoCriado", "StatusPedidoAlterado"]);
        logs.Textos.Should().NotContain(t => t.Contains(email, StringComparison.OrdinalIgnoreCase)
            || t.Contains(token, StringComparison.Ordinal)
            || t.Contains("\"itens\"", StringComparison.Ordinal));
    }

    private static async Task NadaGravadoAsync(string stringConexao)
    {
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM Pedidos")).Should().Be(0);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM ChavesIdempotencia")).Should().Be(0);
    }
}
