using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        var resposta = await worker.CreateClient().GetAsync("/health/pronto", Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
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
    [InlineData("RabbitMQ:Senha", "", "RabbitMQ:Senha")]
    [InlineData("Mensageria:TamanhoLote", "0", "Mensageria:TamanhoLote")]
    public void Configuracao_invalida_impede_o_Worker_de_subir_sem_mostrar_valores(string chave, string valor, string esperado)
    {
        var broker = new Dictionary<string, string?>
        {
            ["RabbitMQ:Servidor"] = "127.0.0.1",
            ["RabbitMQ:Porta"] = "1",
            ["RabbitMQ:Usuario"] = "usuario-secreto",
            ["RabbitMQ:Senha"] = "senha-secreta",
            ["RabbitMQ:VirtualHost"] = "/"
        };
        using var logs = new CapturaLogs();
        using var worker = new FabricaWorker
        {
            StringConexao = FabricaApi.StringConexaoTeste,
            ConfiguracoesBroker = broker,
            Configuracoes = new Dictionary<string, string?> { [chave] = valor },
            Servicos = servicos => servicos.AddSingleton<ILoggerProvider>(logs)
        };

        var excecao = Record.Exception(() => worker.CreateClient());

        // A falha de partida faz o Main liberar o host; se o WebApplicationFactory consultar os
        // serviços depois disso, a exceção que chega aqui é ObjectDisposedException, e não a de
        // validação (intermitente no CI, PR #22). O log "Hosting failed to start" (EventId 11) é
        // gravado antes da liberação e traz sempre a falha original.
        excecao.Should().NotBeNull();
        var falhaPartida = logs.Registros.Where(r => r.Evento.Id == 11).Select(r => r.Texto).Should().ContainSingle().Subject;
        falhaPartida.Should().Contain(esperado);
        var mensagens = string.Join(" | ", PreparacaoInicializacaoTestes.Cadeia(excecao!).Select(e => e.Message).Append(falhaPartida));
        mensagens.Should().NotContain("usuario-secreto").And.NotContain("senha-secreta");
    }
}
