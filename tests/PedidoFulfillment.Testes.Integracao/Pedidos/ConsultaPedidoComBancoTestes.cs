using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>User Story 3 com SQL Server real (cenários 1 a 4).</summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class ConsultaPedidoComBancoTestes(BancoDadosTeste banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Dono_e_operacao_veem_o_pedido_e_os_demais_recebem_o_mesmo_404()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var (dono, _, _) = await ClienteAsync(api);
        var (outro, _, _) = await ClienteAsync(api);
        var criado = await CriadoAsync(dono, (Cat001, 1));
        var rota = $"{Rota}/{criado.GetProperty("id").GetGuid()}";

        foreach (var cliente in new[] { dono, await AdminAsync(api), await OperadorAsync(api) })
        {
            var resposta = await cliente.GetAsync(rota, Cancelamento);
            resposta.StatusCode.Should().Be(HttpStatusCode.OK);
            (await CorpoAsync(resposta)).GetProperty("numero").GetString().Should().Be(criado.GetProperty("numero").GetString());
        }

        var deOutro = await outro.GetAsync(rota, Cancelamento);
        var inexistente = await dono.GetAsync($"{Rota}/{Guid.NewGuid()}", Cancelamento);
        deOutro.StatusCode.Should().Be(HttpStatusCode.NotFound);
        inexistente.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var (corpoOutro, corpoInexistente) = (await CorpoAsync(deOutro), await CorpoAsync(inexistente));
        corpoOutro.GetProperty("title").GetString().Should().Be(corpoInexistente.GetProperty("title").GetString());
        corpoOutro.GetProperty("detail").GetString().Should().Be(corpoInexistente.GetProperty("detail").GetString());
        (await dono.GetAsync($"{Rota}/nao-e-guid", Cancelamento)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>REQ-11, User Story 1: histórico sem autor, motivo da operação visível ao Cliente e entrega.</summary>
    [Fact]
    public async Task Dono_ve_o_historico_sem_autor_e_a_entrega_do_pedido()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (dono, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var pedidoId = (await CriadoAsync(dono, (Cat001, 1))).GetProperty("id").GetGuid();
        var semOrdemId = (await CriadoAsync(dono, (Cat002, 1))).GetProperty("id").GetGuid();
        (await MudarStatusAsync(operador, pedidoId, "Processando", "Conferido pela operação.")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await MudarStatusAsync(operador, pedidoId, "ProntoParaEnvio")).StatusCode.Should().Be(HttpStatusCode.OK);
        var criadaEm = new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
        await BancoDadosTeste.ExecutarAsync(stringConexao,
            $"INSERT INTO OrdensEntrega (Id, PedidoId, CriadoEm, AtualizadoEm) VALUES ('{Guid.CreateVersion7()}', '{pedidoId}', '{criadaEm:O}', '{criadaEm:O}')");

        var corpo = await CorpoAsync(await dono.GetAsync($"{Rota}/{pedidoId}", Cancelamento));

        var historico = corpo.GetProperty("historico").EnumerateArray().ToList();
        historico.Select(h => (Anterior(h), h.GetProperty("statusNovo").GetString())).Should().Equal(
            ((string?)null, "Pendente"), ("Pendente", "Processando"), ("Processando", "ProntoParaEnvio"));
        historico[1].GetProperty("motivo").GetString().Should().Be("Conferido pela operação.");
        historico.SelectMany(h => h.EnumerateObject()).Select(p => p.Name).Distinct()
            .Should().BeEquivalentTo(["statusAnterior", "statusNovo", "ocorridoEm", "motivo"], "o autor não aparece (FR-003)");
        var entrega = corpo.GetProperty("entrega");
        entrega.GetProperty("criadoEm").GetDateTimeOffset().Should().Be(criadaEm);
        entrega.GetProperty("transportadora").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        entrega.GetProperty("codigoRastreio").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        entrega.GetProperty("despachadoEm").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);

        var semOrdem = await CorpoAsync(await dono.GetAsync($"{Rota}/{semOrdemId}", Cancelamento));
        semOrdem.GetProperty("entrega").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Null);
        semOrdem.GetProperty("historico").GetArrayLength().Should().Be(1);
    }

    private static string? Anterior(System.Text.Json.JsonElement entrada) =>
        entrada.GetProperty("statusAnterior").ValueKind == System.Text.Json.JsonValueKind.Null ? null : entrada.GetProperty("statusAnterior").GetString();
}
