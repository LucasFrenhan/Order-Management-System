using System.Net;
using FluentAssertions;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Integracao.Autenticacao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Mensageria;

/// <summary>
/// User Story 4: o processo de workers fica pronto com banco e broker, declara a troca de eventos
/// e recusa configuração inválida sem mostrar valores (REQ-06, FR-017, FR-018). A API sem o
/// Worker está coberta em <see cref="OutboxComBrokerTestes"/>.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class WorkerTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Worker_fica_pronto_e_declara_a_troca_de_eventos()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        await using var worker = new FabricaWorker
        {
            StringConexao = await mensageria.NovoBancoMigradoAsync(),
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker
        };

        // Desde o REQ-17 (R-06), o Worker só fica pronto com os consumidores inscritos.
        var cliente = worker.CreateClient();
        (await MensageriaTeste.EsperarAteAsync(
            async () => (await cliente.GetAsync("/health/pronto", Cancelamento)).StatusCode == HttpStatusCode.OK,
            TimeSpan.FromSeconds(30))).Should().BeTrue("com banco, broker e consumidores no ar, o Worker fica pronto");
        await using var conexao = await mensageria.ConectarAsync();
        (await MensageriaTeste.EsperarAteAsync(async () =>
        {
            try
            {
                await using var canal = await conexao.CreateChannelAsync(cancellationToken: Cancelamento);
                await canal.ExchangeDeclarePassiveAsync(TopologiaMensageria.TrocaEventos, Cancelamento);
                return true;
            }
            catch (RabbitMQ.Client.Exceptions.OperationInterruptedException)
            {
                return false;
            }
        }, TimeSpan.FromSeconds(5))).Should().BeTrue();
    }

    [Theory]
    [InlineData("RabbitMQ:Senha", "", "RabbitMQ:Senha", null)]
    [InlineData("Mensageria:TamanhoLote", "0", "Mensageria:TamanhoLote", null)]
    [InlineData("GatewayPagamento:TaxaAprovacaoPercentual", "150", "GatewayPagamento:TaxaAprovacaoPercentual", "150")]
    public void Configuracao_invalida_impede_o_Worker_de_subir_sem_mostrar_valores(string chave, string valor, string esperado, string? oculto)
    {
        var broker = new Dictionary<string, string?>
        {
            ["RabbitMQ:Servidor"] = "127.0.0.1",
            ["RabbitMQ:Porta"] = "1",
            ["RabbitMQ:Usuario"] = "usuario-secreto",
            ["RabbitMQ:Senha"] = "senha-secreta",
            ["RabbitMQ:VirtualHost"] = "/"
        };
        using var worker = new FabricaWorker
        {
            StringConexao = FabricaApi.StringConexaoTeste,
            ConfiguracoesBroker = broker,
            Configuracoes = new Dictionary<string, string?> { [chave] = valor }
        };

        var excecao = Record.Exception(() => worker.CreateClient());

        excecao.Should().NotBeNull();
        var mensagens = string.Join(" | ", worker.FalhaPartida.Cadeia(excecao!).Select(e => e.Message));
        mensagens.Should().Contain(esperado).And.NotContain("usuario-secreto").And.NotContain("senha-secreta");
        if (oculto is not null)
        {
            mensagens.Should().NotContain(oculto, "o valor configurado não aparece no erro (REQ-09, FR-005)");
        }
    }
}
