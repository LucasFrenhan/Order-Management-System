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
/// Os consumidores voltam sozinhos depois de perder a inscrição, com o Worker no ar o tempo
/// todo (REQ-17; issue #41; contracts/recuperacao-consumo.md): pedidos, DLQ, credenciais e
/// encerramento durante a queda. O broker é parado e iniciado de verdade, numa coleção própria.
/// </summary>
[Collection(nameof(ColecaoQuedaBroker))]
public sealed class RecuperacaoConsumoTestes(MensageriaTeste mensageria) : CenarioRecuperacaoConsumo(mensageria)
{
    /// <summary>
    /// Reprodução da issue #41 (FR-001, T003; US1, cenários 1 e 2): um pedido criado com o broker
    /// fora deixa um evento pendente, e o despachante pede a conexão durante a queda.
    /// </summary>
    [Fact]
    public async Task Pedido_criado_com_o_broker_fora_segue_quando_ele_volta()
    {
        Assert.SkipUnless(Mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = Mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var worker = await CenarioEntrega.WorkerProntoAsync(Mensageria, stringConexao);

        await Mensageria.PararBrokerAsync();
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        await Task.Delay(TimeSpan.FromSeconds(10), Cancelamento);
        await Mensageria.IniciarBrokerAsync();

        (await UmConsumidorEmCadaFilaAsync(PrazoVolta)).Should().BeTrue("os três consumidores voltam sem reiniciar o Worker (FR-003)");
        (await ProntoParaEnvioAsync(stringConexao, pedidoId)).Should().BeTrue("o pedido segue o fluxo sem ação manual");
        (await CenarioEntrega.OrdensAsync(stringConexao, pedidoId)).Should().Be(1);
    }

    [Fact]
    public async Task Mensagem_em_processamento_na_queda_nao_repete_o_efeito()
    {
        // FR-008, FR-009, SC-003 (analyze U1): a reserva grava, segura antes de retornar, o broker
        // cai, o commit acontece com o canal fechado e o ack falha; a reentrega é duplicada.
        Assert.SkipUnless(Mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = Mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var portao = new PortaoSalvar();
        using var logs = new CapturaLogs();
        await using var worker = await WorkerCompletoAsync(stringConexao, logs, servicos =>
            servicos.AddScoped<IArmazenamentoPedidos>(sp => new ArmazenamentoQueSegura(ActivatorUtilities.CreateInstance<ArmazenamentoPedidos>(sp), portao)));

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        await portao.Segurando.Task.WaitAsync(PrazoVolta, Cancelamento);
        await Mensageria.PararBrokerAsync();
        portao.Liberar.TrySetResult();
        await Task.Delay(TimeSpan.FromSeconds(2), Cancelamento);
        await Mensageria.IniciarBrokerAsync();

        (await ProntoParaEnvioAsync(stringConexao, pedidoId)).Should().BeTrue();
        (await EsperarLogAsync(logs, 5005, PrazoVolta)).Should().BeTrue("a reentrega do pedido.criado é descartada como duplicada");
        (await EstoqueAsync(stringConexao, Cat001)).Should().Be(49, "uma reserva só");
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM EventosSaida WHERE Tipo = '{TiposEvento.EstoqueReservado}'")).Should().Be(1);
    }

    [Fact]
    public async Task Monitor_e_reenvio_da_dlq_funcionam_depois_da_queda()
    {
        // FR-011 (analyze A2). O despachante depois da queda está coberto pelo primeiro teste.
        Assert.SkipUnless(Mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await Mensageria.NovoBancoMigradoAsync();
        await PrepararFilasAsync(Mensageria);
        using var logs = new CapturaLogs();
        await using var worker = WorkerReenviando(Mensageria, stringConexao, logs, new Dictionary<string, string?>
        {
            // Os mínimos das opções: avisa acima de 1 mensagem, lendo a cada 10 s.
            ["ReenvioDlq:AvisoHabilitado"] = "true",
            ["ReenvioDlq:AvisoLimite"] = "1",
            ["ReenvioDlq:AvisoIntervaloSegundos"] = "10"
        });
        worker.CreateClient().Dispose();

        await Mensageria.ReiniciarBrokerAsync(TimeSpan.FromSeconds(5));
        var avisosAntes = logs.Registros.Count(r => r.Evento.Id == 5011);
        string[] ids = [await NaDlqAsync(Mensageria), await NaDlqAsync(Mensageria)];

        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(logs.Registros.Count(r => r.Evento.Id == 5011) > avisosAntes), PrazoVolta))
            .Should().BeTrue("o monitor volta a ler a DLQ");
        var pedido = await PedidoAsync(stringConexao);
        (await EsperarSituacaoAsync(stringConexao, pedido.Id, SituacaoReenvioDlq.Concluido)).Should().BeTrue("o reenvio volta a funcionar");
        (await EsvaziarAsync(Mensageria, Principal)).Select(m => m.MessageId).Should().Equal(ids);
    }

    [Fact]
    public async Task Encerrar_o_worker_durante_a_recuperacao_nao_trava()
    {
        // US2, cenário 3.
        Assert.SkipUnless(Mensageria.DockerDisponivel, "Docker indisponível");
        using var logs = new CapturaLogs();
        var worker = await CenarioEntrega.WorkerProntoAsync(Mensageria, await Mensageria.NovoBancoMigradoAsync(), logs);
        try
        {
            await Mensageria.PararBrokerAsync();
            (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(logs.Registros.Count(r => r.Evento.Id == 5016) >= 3), PrazoVolta)).Should().BeTrue();

            var relogio = System.Diagnostics.Stopwatch.StartNew();
            await worker.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), Cancelamento);
            var registrosNoFim = logs.Registros.Count;
            relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
            await Task.Delay(TimeSpan.FromSeconds(2), Cancelamento);
            logs.Registros.Should().HaveCount(registrosNoFim, "nada mais tenta se inscrever depois do encerramento");
            logs.Registros.Should().NotContain(r => r.Evento.Id == 5018);
        }
        finally
        {
            await Mensageria.IniciarBrokerAsync();
        }
    }

    [Fact]
    public async Task Credenciais_recusadas_mantem_o_worker_nao_pronto_ate_voltarem()
    {
        // Spec, Edge Cases (analyze G1).
        Assert.SkipUnless(Mensageria.DockerDisponivel, "Docker indisponível");
        const string OutraSenha = "outra-senha-req17";
        using var logs = new CapturaLogs();
        await using var worker = await CenarioEntrega.WorkerProntoAsync(Mensageria, await Mensageria.NovoBancoMigradoAsync(), logs);
        var http = worker.CreateClient();
        try
        {
            await Mensageria.TrocarSenhaAsync(OutraSenha);
            await Mensageria.FecharConexoesAsync();

            (await EsperarLogAsync(logs, 5017, PrazoVolta)).Should().BeTrue("as tentativas falham com a senha recusada");
            var limite = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < limite)
            {
                var prontidao = await ProntidaoAsync(http);
                prontidao.Codigo.Should().Be(HttpStatusCode.ServiceUnavailable);
                prontidao.Consumidores.Should().Be("Unhealthy");
                await Task.Delay(500, Cancelamento);
            }
        }
        finally
        {
            await Mensageria.RestaurarSenhaAsync();
        }

        (await UmConsumidorEmCadaFilaAsync(PrazoVolta)).Should().BeTrue("com a senha de volta, os consumidores voltam");
        logs.Registros.Should().NotContain(r => r.Texto.Contains(OutraSenha, StringComparison.Ordinal) || r.Texto.Contains(MensageriaTeste.SenhaBroker, StringComparison.Ordinal));
    }
}
