using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>
/// Despacho e correção do rastreio com SQL Server real (REQ-11, User Stories 2 e 3;
/// contracts/rastreio-http.md).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class RastreioPedidoComBancoTestes(BancoDadosTeste banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    /// <summary>API com um Cliente, o Admin e o operador.</summary>
    private async Task<Cenario> CenarioAsync()
    {
        var stringConexao = banco.NovoBanco();
        var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        return new Cenario(stringConexao, api, cliente, await AdminAsync(api), await OperadorAsync(api));
    }

    /// <summary>Pedido do Cliente levado pela operação até o status; com ordem, ela é inserida por SQL.</summary>
    private static async Task<Guid> PedidoAsync(Cenario c, string status = "ProntoParaEnvio", bool comOrdem = true)
    {
        var pedidoId = (await CriadoAsync(c.Cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        string[] caminho = status switch
        {
            "Pendente" => [],
            "Processando" => ["Processando"],
            "ProntoParaEnvio" => ["Processando", "ProntoParaEnvio"],
            "Despachado" => ["Processando", "ProntoParaEnvio", "Despachado"],
            "Cancelado" => ["Cancelado"],
            _ => throw new ArgumentOutOfRangeException(nameof(status))
        };
        foreach (var passo in caminho)
        {
            (await MudarStatusAsync(c.Operador, pedidoId, passo, passo == "Cancelado" ? "Teste" : null)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        if (comOrdem)
        {
            await BancoDadosTeste.ExecutarAsync(c.StringConexao,
                $"INSERT INTO OrdensEntrega (Id, PedidoId, CriadoEm, AtualizadoEm) VALUES ('{Guid.CreateVersion7()}', '{pedidoId}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())");
        }

        return pedidoId;
    }

    private static Task<string?> RastreioGravadoAsync(Cenario c, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<string?>(c.StringConexao,
            $"SELECT ISNULL(Transportadora, '-') + '/' + ISNULL(CodigoRastreio, '-') FROM OrdensEntrega WHERE PedidoId = '{pedidoId}'");

    private static Task<int> OrdensAsync(Cenario c, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<int>(c.StringConexao, $"SELECT COUNT(*) FROM OrdensEntrega WHERE PedidoId = '{pedidoId}'");

    private static async Task<string> StatusAsync(Cenario c, Guid pedidoId) =>
        (await CorpoAsync(await c.Operador.GetAsync($"{Rota}/{pedidoId}", Cancelamento))).GetProperty("status").GetString()!;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Operacao_despacha_o_pedido_pronto_com_o_rastreio(bool comoAdmin)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var c = await CenarioAsync();
        await using var _ = c.Api;
        var pedidoId = await PedidoAsync(c);

        var resposta = await RegistrarRastreioAsync(comoAdmin ? c.Admin : c.Operador, pedidoId, "  Correios ", "BR123456789BR");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var corpo = await CorpoAsync(resposta);
        corpo.GetProperty("status").GetString().Should().Be("Despachado");
        var ultima = corpo.GetProperty("historico").EnumerateArray().Last();
        (ultima.GetProperty("statusAnterior").GetString(), ultima.GetProperty("statusNovo").GetString(), ultima.GetProperty("motivo").GetString())
            .Should().Be(("ProntoParaEnvio", "Despachado", "Pedido despachado."));
        var entrega = corpo.GetProperty("entrega");
        (entrega.GetProperty("transportadora").GetString(), entrega.GetProperty("codigoRastreio").GetString()).Should().Be(("Correios", "BR123456789BR"));
        entrega.GetProperty("despachadoEm").ValueKind.Should().Be(JsonValueKind.String);
        var consulta = await CorpoAsync(await c.Cliente.GetAsync($"{Rota}/{pedidoId}", Cancelamento));
        consulta.GetProperty("entrega").GetProperty("codigoRastreio").GetString().Should().Be("BR123456789BR");
        consulta.GetProperty("status").GetString().Should().Be("Despachado");
        (await OrdensAsync(c, pedidoId)).Should().Be(1);
    }

    [Fact]
    public async Task Pedido_sem_ordem_ganha_a_ordem_no_despacho_sem_anunciar_entrega_criada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var c = await CenarioAsync();
        await using var _ = c.Api;
        var pedidoId = await PedidoAsync(c, comOrdem: false);

        (await RegistrarRastreioAsync(c.Operador, pedidoId, "Correios", "BR1")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await OrdensAsync(c, pedidoId)).Should().Be(1);
        (await RastreioGravadoAsync(c, pedidoId)).Should().Be("Correios/BR1");
        (await BancoDadosTeste.ConsultarAsync<int>(c.StringConexao,
            $"SELECT COUNT(*) FROM EventosSaida WHERE Tipo = 'entrega.criada' AND Conteudo LIKE '%{pedidoId}%'")).Should().Be(0);
    }

    [Fact]
    public async Task Cliente_e_anonimo_nao_despacham_e_pedido_inexistente_e_404()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var c = await CenarioAsync();
        await using var _ = c.Api;
        var pedidoId = await PedidoAsync(c);

        (await RegistrarRastreioAsync(c.Cliente, pedidoId, "Correios", "BR1")).StatusCode.Should().Be(HttpStatusCode.Forbidden, "nem o dono despacha");
        (await RegistrarRastreioAsync(c.Api.CreateClient(), pedidoId, "Correios", "BR1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await RegistrarRastreioAsync(c.Operador, Guid.NewGuid(), "Correios", "BR1")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await c.Operador.PatchAsJsonAsync($"{Rota}/nao-e-guid/rastreio", new { transportadora = "Correios", codigoRastreio = "BR1" }, Cancelamento))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StatusAsync(c, pedidoId)).Should().Be("ProntoParaEnvio");
        (await RastreioGravadoAsync(c, pedidoId)).Should().Be("-/-");
    }

    [Theory]
    [InlineData("", "BR1", "transportadora")]
    [InlineData("   ", "BR1", "transportadora")]
    [InlineData(null, "BR1", "transportadora")]
    [InlineData("101", "BR1", "transportadora")]
    [InlineData("Correios", "51", "codigoRastreio")]
    [InlineData("Correios", "  ", "codigoRastreio")]
    public async Task Dados_invalidos_sao_recusados_com_o_campo(string? transportadora, string? codigo, string campo)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var c = await CenarioAsync();
        await using var _ = c.Api;
        var pedidoId = await PedidoAsync(c);
        transportadora = transportadora == "101" ? new string('T', 101) : transportadora;
        codigo = codigo == "51" ? new string('C', 51) : codigo;

        var resposta = await RegistrarRastreioAsync(c.Operador, pedidoId, transportadora, codigo);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CorpoAsync(resposta)).GetProperty("erros").EnumerateObject().Select(e => e.Name)
            .Should().Contain(n => n.Equals(campo, StringComparison.OrdinalIgnoreCase));
        (await StatusAsync(c, pedidoId)).Should().Be("ProntoParaEnvio");
    }

    [Theory]
    [InlineData("Pendente")]
    [InlineData("Processando")]
    [InlineData("Cancelado")]
    public async Task Pedido_fora_de_pronto_ou_despachado_e_transicao_invalida(string status)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var c = await CenarioAsync();
        await using var _ = c.Api;
        var pedidoId = await PedidoAsync(c, status);

        var resposta = await RegistrarRastreioAsync(c.Operador, pedidoId, "Correios", "BR1");

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(resposta)).Should().Be("PEDIDO_TRANSICAO_INVALIDA");
        (await StatusAsync(c, pedidoId)).Should().Be(status);
        (await RastreioGravadoAsync(c, pedidoId)).Should().Be("-/-");
    }

    [Fact]
    public async Task Rastreio_de_outro_pedido_com_outra_caixa_e_duplicado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var c = await CenarioAsync();
        await using var _ = c.Api;
        var primeiro = await PedidoAsync(c);
        var segundo = await PedidoAsync(c);
        (await RegistrarRastreioAsync(c.Operador, primeiro, "Correios", "BR123")).StatusCode.Should().Be(HttpStatusCode.OK);

        var resposta = await RegistrarRastreioAsync(c.Operador, segundo, "correios", "br123");

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(resposta)).Should().Be("PEDIDO_RASTREIO_DUPLICADO");
        (await StatusAsync(c, segundo)).Should().Be("ProntoParaEnvio");
        (await RastreioGravadoAsync(c, segundo)).Should().Be("-/-");
    }

    [Fact]
    public async Task Despacho_pela_rota_de_status_continua_permitido()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var c = await CenarioAsync();
        await using var _ = c.Api;
        var pedidoId = await PedidoAsync(c);

        (await MudarStatusAsync(c.Operador, pedidoId, "Despachado")).StatusCode.Should().Be(HttpStatusCode.OK, "FR-009b");

        (await RastreioGravadoAsync(c, pedidoId)).Should().Be("-/-");
    }

    /// <summary>SC-006: despacho e cancelamento simultâneos terminam num só estado, sem rastreio em pedido cancelado.</summary>
    [Fact]
    public async Task Despacho_e_cancelamento_simultaneos_terminam_num_so_estado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var c = await CenarioAsync();
        await using var _ = c.Api;

        for (var rodada = 1; rodada <= 10; rodada++)
        {
            var pedidoId = await PedidoAsync(c);

            var despacho = RegistrarRastreioAsync(c.Operador, pedidoId, "Correios", $"BR-CORRIDA-{rodada}-{Guid.NewGuid():N}"[..40]);
            var cancelamento = MudarStatusAsync(c.Admin, pedidoId, "Cancelado", "Cliente desistiu");
            await Task.WhenAll(despacho, cancelamento);

            var status = await StatusAsync(c, pedidoId);
            if (status == "Despachado")
            {
                (await despacho).StatusCode.Should().Be(HttpStatusCode.OK, $"rodada {rodada}");
                (await cancelamento).StatusCode.Should().Be(HttpStatusCode.Conflict, $"rodada {rodada}: Despachado é final");
            }
            else
            {
                status.Should().Be("Cancelado", $"rodada {rodada}");
                (await despacho).StatusCode.Should().Be(HttpStatusCode.Conflict, $"rodada {rodada}");
                (await cancelamento).StatusCode.Should().Be(HttpStatusCode.OK, $"rodada {rodada}");
                (await RastreioGravadoAsync(c, pedidoId)).Should().Be("-/-", $"rodada {rodada}: sem rastreio em pedido cancelado");
            }
        }
    }

    private sealed record Cenario(string StringConexao, FabricaApi Api, HttpClient Cliente, HttpClient Admin, HttpClient Operador);
}
