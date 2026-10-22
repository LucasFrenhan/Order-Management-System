using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>User Story 4 com SQL Server real (cenários 1 a 8).</summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class StatusPedidoComBancoTestes(BancoDadosTeste banco)
{
    private const int RepeticoesCorrida = 10;

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Operador_percorre_o_ciclo_e_transicao_invalida_responde_409()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var id = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        foreach (var status in new[] { "Processando", "ProntoParaEnvio", "Despachado" })
        {
            var resposta = await MudarStatusAsync(operador, id, status);
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, status);
            (await CorpoAsync(resposta)).GetProperty("status").GetString().Should().Be(status);
        }

        var invalida = await MudarStatusAsync(operador, id, "Pendente");
        invalida.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var corpo = await CorpoAsync(invalida);
        corpo.GetProperty("codigo").GetString().Should().Be("PEDIDO_TRANSICAO_INVALIDA");
        corpo.GetProperty("detail").GetString().Should().Contain("Despachado").And.Contain("Pendente");
        (await CorpoAsync(await cliente.GetAsync($"{Rota}/{id}", Cancelamento))).GetProperty("status").GetString().Should().Be("Despachado");
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM HistoricosStatusPedido WHERE PedidoId = '{id}' AND AutorId IS NOT NULL")).Should().Be(3);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM HistoricosStatusPedido WHERE PedidoId = '{id}'")).Should().Be(4);
    }

    [Fact]
    public async Task Admin_cancela_pedido_pronto_para_envio()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var (cliente, _, _) = await ClienteAsync(api);
        var admin = await AdminAsync(api);
        var id = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        await MudarStatusAsync(admin, id, "Processando");
        await MudarStatusAsync(admin, id, "ProntoParaEnvio");

        (await MudarStatusAsync(admin, id, "Cancelado", "Endereço inválido")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Cliente_dono_cancela_pendente_e_recebe_403_em_processando()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var pendente = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        var processando = (await CriadoAsync(cliente, (Cat002, 1))).GetProperty("id").GetGuid();
        await MudarStatusAsync(operador, processando, "Processando");

        var cancelado = await MudarStatusAsync(cliente, pendente, "Cancelado", "Desisti");
        var recusado = await MudarStatusAsync(cliente, processando, "Cancelado", "Desisti");

        cancelado.StatusCode.Should().Be(HttpStatusCode.OK);
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao,
            $"SELECT TOP 1 Motivo FROM HistoricosStatusPedido WHERE PedidoId = '{pendente}' ORDER BY Id DESC")).Should().Be("Desisti");
        recusado.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Outro_cliente_e_pedido_inexistente_recebem_404_e_validacao_do_contrato_continua()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var (dono, _, _) = await ClienteAsync(api);
        var (outro, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var id = (await CriadoAsync(dono, (Cat001, 1))).GetProperty("id").GetGuid();

        (await MudarStatusAsync(outro, id, "Cancelado", "x")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await MudarStatusAsync(operador, Guid.NewGuid(), "Processando")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await MudarStatusAsync(operador, id, "PagamentoRejeitado")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await MudarStatusAsync(operador, id, "Cancelado")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Mudancas_simultaneas_nunca_se_sobrescrevem()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);

        for (var repeticao = 0; repeticao < RepeticoesCorrida; repeticao++)
        {
            var id = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

            var respostas = await Task.WhenAll(
                Task.Run(() => MudarStatusAsync(cliente, id, "Cancelado", "Desisti"), Cancelamento),
                Task.Run(() => MudarStatusAsync(operador, id, "Processando"), Cancelamento));

            var (doCliente, doOperador) = (respostas[0].StatusCode, respostas[1].StatusCode);
            var final = (await CorpoAsync(await cliente.GetAsync($"{Rota}/{id}", Cancelamento))).GetProperty("status").GetString();
            if (final == "Cancelado")
            {
                (doCliente, doOperador).Should().Be((HttpStatusCode.OK, HttpStatusCode.Conflict), $"repetição {repeticao}");
            }
            else
            {
                final.Should().Be("Processando");
                (doCliente, doOperador).Should().Be((HttpStatusCode.Forbidden, HttpStatusCode.OK), $"repetição {repeticao}");
            }

            (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM HistoricosStatusPedido WHERE PedidoId = '{id}'"))
                .Should().Be(2, $"repetição {repeticao}");
        }
    }
}
