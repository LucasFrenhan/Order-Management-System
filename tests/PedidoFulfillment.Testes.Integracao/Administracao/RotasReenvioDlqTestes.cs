using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Administracao;

/// <summary>
/// Rotas administrativas do reenvio da DLQ, com SQL Server real (REQ-14, US2 cenários 1, 3 e
/// 9 a 12). A <see cref="FabricaApi"/> aponta o broker para uma porta fechada: as rotas não
/// dependem dele.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class RotasReenvioDlqTestes(MensageriaTeste mensageria)
{
    private const string RotaEstoque = "/api/v1/admin/dlq/estoque/reenvios";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static Task<HttpResponseMessage> PedirAsync(HttpClient cliente, object corpo, string rota = RotaEstoque) =>
        cliente.PostAsJsonAsync(rota, corpo, Cancelamento);

    private static Guid Sub(string token) =>
        Guid.Parse(new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token).Subject);

    [Fact]
    public async Task Admin_registra_o_pedido_e_o_consulta_mesmo_com_o_broker_fora()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        var relogio = Stopwatch.StartNew();

        var resposta = await PedirAsync(admin, new { tipoEvento = "pedido.criado", trechoMotivo = " Timeout " });

        relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1), "SC-007: a rota não espera o broker");
        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var corpo = await CorpoAsync(resposta);
        var id = corpo.GetProperty("id").GetGuid();
        resposta.Headers.Location!.ToString().Should().Be($"/api/v1/admin/dlq/reenvios/{id}");
        corpo.GetProperty("situacao").GetString().Should().Be("Pendente");
        corpo.GetProperty("consumidor").GetString().Should().Be("estoque");
        corpo.GetProperty("trechoMotivo").GetString().Should().Be("Timeout");
        var token = admin.DefaultRequestHeaders.Authorization!.Parameter!;
        corpo.GetProperty("autorId").GetGuid().Should().Be(Sub(token), "o autor é o Admin autenticado");
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT Situacao FROM PedidosReenvioDlq WHERE Id = '{id}'")).Should().Be("Pendente");

        var consulta = await admin.GetAsync(resposta.Headers.Location, Cancelamento);

        consulta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CorpoAsync(consulta)).GetProperty("id").GetGuid().Should().Be(id);
    }

    [Fact]
    public async Task Corpo_vazio_registra_sem_filtro()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        await using var api = Api(mensageria.NovoBanco());

        var resposta = await PedirAsync(await AdminAsync(api), new { }, "/api/v1/admin/dlq/pagamento/reenvios");

        resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var corpo = await CorpoAsync(resposta);
        corpo.GetProperty("tipoEvento").ValueKind.Should().Be(JsonValueKind.Null);
        (corpo.GetProperty("reenviadas").GetInt32(), corpo.GetProperty("foraDoFiltro").GetInt32()).Should().Be((0, 0));
    }

    [Fact]
    public async Task Sem_token_401_e_Cliente_ou_OperadorLogistica_403_nas_duas_rotas()
    {
        // US2 cenário 9, clarify Q2.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var anonimo = api.CreateClient();
        var consulta = $"/api/v1/admin/dlq/reenvios/{Guid.CreateVersion7()}";

        (await PedirAsync(anonimo, new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonimo.GetAsync(consulta, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        foreach (var semPermissao in new[] { cliente, operador })
        {
            (await PedirAsync(semPermissao, new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await semPermissao.GetAsync(consulta, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM PedidosReenvioDlq")).Should().Be(0);
    }

    [Fact]
    public async Task Consumidor_desconhecido_e_filtro_invalido_respondem_400_sem_registrar()
    {
        // US2 cenário 10.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        var agora = DateTimeOffset.UtcNow;

        var desconhecido = await PedirAsync(admin, new { }, "/api/v1/admin/dlq/desconhecido/reenvios");
        var periodo = await PedirAsync(admin, new { falhouDe = agora, falhouAte = agora.AddMinutes(-1) });
        var longo = await PedirAsync(admin, new { trechoMotivo = new string('m', 201) });

        desconhecido.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CorpoAsync(desconhecido)).GetProperty("detail").GetString().Should().Be("O consumidor deve ser estoque, pagamento ou entrega.");
        periodo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CorpoAsync(periodo)).GetProperty("erros").TryGetProperty("falhouAte", out _).Should().BeTrue();
        longo.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CorpoAsync(longo)).GetProperty("erros").TryGetProperty("trechoMotivo", out _).Should().BeTrue();
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM PedidosReenvioDlq")).Should().Be(0);
    }

    [Fact]
    public async Task Consulta_de_pedido_inexistente_404_e_de_id_invalido_400()
    {
        // US2 cenário 12.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        await using var api = Api(mensageria.NovoBanco());
        var admin = await AdminAsync(api);

        var inexistente = await admin.GetAsync($"/api/v1/admin/dlq/reenvios/{Guid.CreateVersion7()}", Cancelamento);
        var invalido = await admin.GetAsync("/api/v1/admin/dlq/reenvios/nao-e-guid", Cancelamento);

        inexistente.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CorpoAsync(inexistente)).GetProperty("title").GetString().Should().Be("Pedido de reenvio não encontrado.");
        invalido.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
