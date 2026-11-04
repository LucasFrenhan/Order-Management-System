using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Entregas;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Administracao.CenarioReenvio;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Mensageria;

/// <summary>
/// Os consumidores voltam sozinhos depois de perder a inscrição (REQ-17; issue #41;
/// contracts/recuperacao-consumo.md): queda longa, quedas seguidas, broker zerado, conexão e
/// fila apagadas, e a prontidão. Coleção própria, separada de <see cref="RecuperacaoConsumoTestes"/>.
/// </summary>
[Collection(nameof(ColecaoQuedaBrokerReinicio))]
public sealed class RecuperacaoBrokerTestes(MensageriaTeste mensageria) : CenarioRecuperacaoConsumo(mensageria)
{
    [Fact]
    public async Task Consumo_volta_depois_de_queda_longa()
    {
        // US1, cenário 4: 30 s parado já leva a espera ao teto de 10 s (analyze C1).
        Assert.SkipUnless(Mensageria.DockerDisponivel, "Docker indisponível");
        await using var worker = await CenarioEntrega.WorkerProntoAsync(Mensageria, await Mensageria.NovoBancoMigradoAsync());

        await Mensageria.ReiniciarBrokerAsync(TimeSpan.FromSeconds(30));

        (await UmConsumidorEmCadaFilaAsync(PrazoVolta)).Should().BeTrue("o prazo conta a partir da volta do broker");
    }

    [Fact]
    public async Task Quedas_seguidas_deixam_um_unico_consumidor_por_fila()
    {
        // US1, cenário 5; FR-006.
        Assert.SkipUnless(Mensageria.DockerDisponivel, "Docker indisponível");
        await using var worker = await CenarioEntrega.WorkerProntoAsync(Mensageria, await Mensageria.NovoBancoMigradoAsync());

        for (var queda = 0; queda < 3; queda++)
        {
            await Mensageria.ReiniciarBrokerAsync(TimeSpan.FromSeconds(2));
        }

        (await UmConsumidorEmCadaFilaAsync(PrazoVolta)).Should().BeTrue();
        var limite = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < limite)
        {
            foreach (var consumidor in Consumidores)
            {
                (await Mensageria.ConsumidoresNaFilaAsync(TopologiaMensageria.FilaPrincipal(consumidor))).Should().Be(1, "nenhuma inscrição duplicada em {0}", consumidor);
            }

            await Task.Delay(250, Cancelamento);
        }
    }

    [Fact]
    public async Task Broker_sem_dados_tem_as_filas_recriadas_antes_da_inscricao()
    {
        // US1, cenário 6; FR-005.
        Assert.SkipUnless(Mensageria.DockerDisponivel, "Docker indisponível");
        await using var worker = await CenarioEntrega.WorkerProntoAsync(Mensageria, await Mensageria.NovoBancoMigradoAsync());

        await Mensageria.RecriarBrokerAsync();

        (await UmConsumidorEmCadaFilaAsync(PrazoVolta)).Should().BeTrue();
        foreach (var consumidor in Consumidores)
        {
            foreach (var fila in new[] { 100, 200, 400 }.Select(ms => TopologiaMensageria.FilaEspera(consumidor, ms)).Append(TopologiaMensageria.FilaMortas(consumidor)))
            {
                (await Mensageria.ConsumidoresNaFilaAsync(fila)).Should().NotBeNull("a fila {0} foi redeclarada", fila);
            }
        }
    }

    [Fact]
    public async Task Fila_apagada_e_recriada_e_o_consumidor_volta()
    {
        // US2, cenário 1: o broker cancela a inscrição, e o canal continua aberto.
        Assert.SkipUnless(Mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = Mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        using var logs = new CapturaLogs();
        await using var worker = await CenarioEntrega.WorkerProntoAsync(Mensageria, stringConexao, logs);

        await Mensageria.ApagarFilasConsumidorAsync("estoque", soPrincipal: true);

        var outrasSempreInscritas = true;
        var voltou = await MensageriaTeste.EsperarAteAsync(async () =>
        {
            foreach (var outro in new[] { "pagamento", "entrega" })
            {
                outrasSempreInscritas &= await Mensageria.ConsumidoresNaFilaAsync(TopologiaMensageria.FilaPrincipal(outro)) == 1;
            }

            return await Mensageria.ConsumidoresNaFilaAsync(TopologiaMensageria.FilaPrincipal("estoque")) == 1;
        }, PrazoVolta);
        voltou.Should().BeTrue("a fila é recriada e o consumidor se inscreve de novo (FR-004, FR-005)");
        outrasSempreInscritas.Should().BeTrue("os outros consumidores não são afetados");
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await ProntoParaEnvioAsync(stringConexao, pedidoId)).Should().BeTrue();
        var perdas = logs.Registros.Where(r => r.Evento.Id == 5016).ToList();
        perdas.Should().ContainSingle().Which.Texto.Should().Contain("estoque").And.Contain("(cancelamento)");
        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 5018).Which.Texto.Should().Contain("estoque");
    }

    [Fact]
    public async Task Conexao_fechada_a_forca_tem_os_tres_consumidores_de_volta()
    {
        // US2, cenário 2.
        Assert.SkipUnless(Mensageria.DockerDisponivel, "Docker indisponível");
        using var logs = new CapturaLogs();
        await using var worker = await CenarioEntrega.WorkerProntoAsync(Mensageria, await Mensageria.NovoBancoMigradoAsync(), logs);

        await Mensageria.FecharConexoesAsync();

        (await EsperarLogAsync(logs, 5016, PrazoVolta)).Should().BeTrue();
        (await UmConsumidorEmCadaFilaAsync(PrazoVolta)).Should().BeTrue();
        logs.Registros.Should().Contain(r => r.Evento.Id == 5019, "a conexão registra o fechamento que o processo não pediu");
    }

    [Fact]
    public async Task Queda_e_volta_aparecem_na_prontidao_e_nos_logs_sem_credenciais()
    {
        // US3, cenários 1 a 4; SC-004; FR-015, FR-016 (analyze C1: uma queda só).
        Assert.SkipUnless(Mensageria.DockerDisponivel, "Docker indisponível");
        using var logs = new CapturaLogs();
        await using var worker = await CenarioEntrega.WorkerProntoAsync(Mensageria, await Mensageria.NovoBancoMigradoAsync(), logs);
        var http = worker.CreateClient();
        (await MensageriaTeste.EsperarAteAsync(async () => (await ProntidaoAsync(http)).Codigo == HttpStatusCode.OK, PrazoVolta)).Should().BeTrue();
        (await ProntidaoAsync(http)).Consumidores.Should().Be("Healthy");

        await Mensageria.PararBrokerAsync();
        (await MensageriaTeste.EsperarAteAsync(async () => (await ProntidaoAsync(http)).Consumidores == "Unhealthy", TimeSpan.FromSeconds(15))).Should().BeTrue();
        for (var leitura = 0; leitura < 5; leitura++)
        {
            var prontidao = await ProntidaoAsync(http);
            prontidao.Codigo.Should().Be(HttpStatusCode.ServiceUnavailable, "toda leitura até a volta é não pronta");
            prontidao.Corpo.Should().NotContain(MensageriaTeste.SenhaBroker).And.NotContain("oms.estoque").And.NotContain(Mensageria.ConfiguracoesBroker["RabbitMQ:Servidor"]!);
            await Task.Delay(200, Cancelamento);
        }

        await Mensageria.IniciarBrokerAsync();

        (await MensageriaTeste.EsperarAteAsync(async () => (await ProntidaoAsync(http)).Codigo == HttpStatusCode.OK, PrazoVolta)).Should().BeTrue("volta a pronta em até 1 min");
        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(Consumidores.All(c => logs.Registros.Any(r => r.Evento.Id == 5018 && r.Texto.Contains(c, StringComparison.Ordinal)))), PrazoVolta))
            .Should().BeTrue();
        var registros = logs.Registros.ToList();
        registros.Should().Contain(r => r.Evento.Id == 5019 && r.Nivel == LogLevel.Warning);
        foreach (var consumidor in Consumidores)
        {
            registros.Should().ContainSingle(r => r.Evento.Id == 5016 && r.Texto.Contains($"Consumidor {consumidor} ", StringComparison.Ordinal))
                .Which.Texto.Should().Contain("(canal)");
            registros.Should().Contain(r => r.Evento.Id == 5017 && r.Nivel == LogLevel.Warning && r.Texto.Contains($"Consumidor {consumidor} ", StringComparison.Ordinal));
            var volta = registros.Should().ContainSingle(r => r.Evento.Id == 5018 && r.Texto.Contains($"Consumidor {consumidor} ", StringComparison.Ordinal)).Subject;
            volta.Nivel.Should().Be(LogLevel.Information);
            volta.Texto.Should().NotContain("depois de 0 ms");
        }

        registros.Should().NotContain(r => r.Texto.Contains(MensageriaTeste.SenhaBroker, StringComparison.Ordinal));
    }
}
