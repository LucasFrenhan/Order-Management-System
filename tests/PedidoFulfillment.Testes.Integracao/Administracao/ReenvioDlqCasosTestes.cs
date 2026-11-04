using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using RabbitMQ.Client;
using static PedidoFulfillment.Testes.Integracao.Administracao.CenarioReenvio;

namespace PedidoFulfillment.Testes.Integracao.Administracao;

/// <summary>
/// Casos do <see cref="ReenvioDlq"/> chamados direto, sem esperar a rodada do executor: DLQ que
/// não existe e mensagem que já foi reenviada pelo mesmo pedido (REQ-14, R-04).
/// </summary>
[Collection(nameof(ColecaoMensageriaReconciliacao))]
public sealed class ReenvioDlqCasosTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static PedidoReenvioDlq Pedido() =>
        PedidoReenvioDlq.Registrar(Guid.CreateVersion7(), Consumidor, new FiltroReenvio(null, null, null, null), DateTimeOffset.UtcNow);

    private async Task<FabricaWorker> WorkerAsync()
    {
        var worker = WorkerReenviando(mensageria, await mensageria.NovoBancoMigradoAsync(),
            configuracoes: new Dictionary<string, string?> { ["ReenvioDlq:Habilitado"] = "false" });
        worker.CreateClient().Dispose();
        return worker;
    }

    [Fact]
    public async Task Dlq_que_nao_existe_nao_tem_nada_a_reenviar()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        await PrepararFilasAsync(mensageria);
        await using (var conexao = await mensageria.ConectarAsync())
        await using (var canal = await conexao.CreateChannelAsync(cancellationToken: Cancelamento))
        {
            await canal.QueueDeleteAsync(Dlq, cancellationToken: Cancelamento);
        }

        try
        {
            await using var worker = await WorkerAsync();
            var reenvio = worker.Services.GetRequiredService<ReenvioDlq>();
            await using var canalReenvio = await reenvio.AbrirCanalAsync(Cancelamento);

            (await reenvio.ExecutarAsync(canalReenvio, Pedido(), Cancelamento)).Should().Be(new ContagensReenvio(0, 0, 0));
        }
        finally
        {
            await PrepararFilasAsync(mensageria);
        }
    }

    [Fact]
    public async Task Mensagem_ja_reenviada_pelo_mesmo_pedido_fica_na_dlq()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        await PrepararFilasAsync(mensageria);
        var pedido = Pedido();
        var id = Guid.CreateVersion7().ToString();
        await using (var conexao = await mensageria.ConectarAsync())
        await using (var canal = await conexao.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), Cancelamento))
        {
            var propriedades = new BasicProperties
            {
                MessageId = id,
                Type = "pedido.criado",
                Headers = new Dictionary<string, object?> { [CabecalhosReenvio.PedidoReenvio] = Encoding.UTF8.GetBytes(pedido.Id.ToString()) }
            };
            await canal.BasicPublishAsync("", Dlq, false, propriedades, "{}"u8.ToArray(), Cancelamento);
        }

        await using var worker = await WorkerAsync();
        var reenvio = worker.Services.GetRequiredService<ReenvioDlq>();
        await using var canalReenvio = await reenvio.AbrirCanalAsync(Cancelamento);

        var contagens = await reenvio.ExecutarAsync(canalReenvio, pedido, Cancelamento);

        contagens.Should().Be(new ContagensReenvio(0, 0, 0));
        (await EsvaziarAsync(mensageria, Dlq)).Select(m => m.MessageId).Should().Equal(id);
        (await ContarAsync(mensageria, Principal)).Should().Be(0);
    }
}
