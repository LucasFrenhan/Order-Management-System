using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Mensageria;

/// <summary>
/// A lista de consumidores que a API aceita no reenvio da DLQ é a mesma que o Worker registra
/// (REQ-14, R-03): um consumidor novo no Worker não fica de fora.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class ConsumidoresConhecidosTestes(MensageriaTeste mensageria)
{
    [Fact]
    public async Task Os_consumidores_do_Worker_sao_os_consumidores_conhecidos()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        await using var worker = new FabricaWorker
        {
            StringConexao = await mensageria.NovoBancoMigradoAsync(),
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker
        };

        var registrados = worker.Services.GetServices<IDescritorConsumidor>().Select(d => d.Consumidor);

        registrados.Should().BeEquivalentTo(ConsumidoresConhecidos.Nomes);
    }
}
