using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Notificacoes.CenarioWebhooks;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Notificacoes;

/// <summary>
/// Um destino que passou a apontar para a rede interna não recebe chamada: o IP é conferido na
/// conexão (REQ-18, FR-016, SC-006).
/// </summary>
[Collection(nameof(ColecaoMensageriaEntregas))]
public sealed class SsrfEnvioTestes(MensageriaTeste mensageria)
{
    [Fact]
    public async Task Destino_que_resolve_para_loopback_nao_recebe_e_o_log_10009_sai()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var receptor = await ReceptorWebhookTeste.IniciarAsync();
        // Cadastrado com a rede local liberada na API; depois o Worker roda sem ela, como em produção.
        var (destinoId, _) = await DestinoAsync(cliente, receptor.Url().Replace("127.0.0.1", "localhost", StringComparison.Ordinal));
        await PedidoComAvisoAsync(cliente, operador);
        var logs = new CapturaLogs();

        await using var worker = WorkerEnviando(mensageria, stringConexao, logs,
            new Dictionary<string, string?> { ["Notificacoes:PermitirRedeLocal"] = "false" });
        worker.CreateClient().Dispose();

        (await MensageriaTeste.EsperarAteAsync(async () =>
            await TentativasAsync(stringConexao, destinoId) >= 1, Prazo)).Should().BeTrue();
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT UltimoErro FROM EntregasNotificacao WHERE DestinoId = '{destinoId}'"))
            .Should().Be("endereço recusado");
        logs.Registros.Should().Contain(r => r.Evento.Id == 10009);
        receptor.Recebidas.Should().BeEmpty();
    }
}
