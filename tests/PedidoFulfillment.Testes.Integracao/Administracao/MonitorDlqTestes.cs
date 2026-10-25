using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Administracao.CenarioReenvio;

namespace PedidoFulfillment.Testes.Integracao.Administracao;

/// <summary>Aviso de tamanho da DLQ, com RabbitMQ real (REQ-14, US3). A verificação é chamada direto.</summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class MonitorDlqTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static MonitorDlq Monitor(FabricaWorker worker) =>
        worker.Services.GetServices<IHostedService>().OfType<MonitorDlq>().Single();

    [Fact]
    public async Task Acima_do_limite_avisa_e_no_limite_nao()
    {
        // US3 cenários 1 e 2.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        await PrepararFilasAsync(mensageria);
        for (var i = 0; i < 3; i++)
        {
            await NaDlqAsync(mensageria);
        }

        var logs = new CapturaLogs();
        await using var worker = WorkerReenviando(mensageria, stringConexao, logs);
        var monitor = Monitor(worker);

        await monitor.VerificarAsync(new OpcoesReenvioDlq { AvisoLimite = 3 }, Cancelamento);
        logs.Registros.Should().NotContain(r => r.Evento.Id == 5011, "no limite não avisa");

        await monitor.VerificarAsync(new OpcoesReenvioDlq { AvisoLimite = 2 }, Cancelamento);
        var aviso = logs.Registros.Should().ContainSingle(r => r.Evento.Id == 5011).Subject;
        aviso.Texto.Should().Contain("estoque").And.Contain("3 mensagem(ns)").And.Contain("limite de 2");
        await PrepararFilasAsync(mensageria);
    }

    [Fact]
    public async Task Broker_fora_registra_a_falha_sem_derrubar_o_Worker()
    {
        // US3 cenário 3.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        var logs = new CapturaLogs();
        await using var worker = WorkerReenviando(mensageria, stringConexao, logs, new Dictionary<string, string?> { ["RabbitMQ:Porta"] = "1" });

        await Monitor(worker).VerificarAsync(new OpcoesReenvioDlq(), Cancelamento);

        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 5012);
        (await worker.CreateClient().GetAsync("/health", Cancelamento)).IsSuccessStatusCode.Should().BeTrue();
    }
}
