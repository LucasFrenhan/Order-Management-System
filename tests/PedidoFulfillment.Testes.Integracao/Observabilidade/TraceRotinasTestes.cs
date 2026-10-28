using System.Diagnostics;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Entregas;
using PedidoFulfillment.Application.Estoque;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Infrastructure.Reconciliacao;
using PedidoFulfillment.Testes.Integracao.Estoque;
using PedidoFulfillment.Testes.Integracao.Estornos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Reconciliacao;
using static PedidoFulfillment.Testes.Integracao.Observabilidade.CenarioObservabilidade;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Observabilidade;

/// <summary>
/// User Story 3 do REQ-16 (T020): cada rodada efetiva é um trace, e cada pedido tratado é um
/// passo com link para o trace de criação dele.
/// </summary>
[Collection(nameof(ColecaoObservabilidade))]
public sealed class TraceRotinasTestes(MensageriaTeste mensageria)
{
    private static readonly TimeSpan UmaHora = TimeSpan.FromHours(1);

    [Fact]
    public async Task Reconciliacao_liga_o_passo_ao_trace_de_criacao_e_o_evento_gravado_continua_o_passo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        // Sem provedor de teste anterior ainda vivo, para o pedido "sem contexto" sair mesmo sem.
        await EsperarSemOuvintesAsync();
        // Pedido criado com a telemetria desligada: o passo sai sem link.
        Guid semContexto;
        await using (var apiSem = Api(stringConexao, null))
        {
            var (clienteSem, _, _) = await ClienteAsync(apiSem);
            semContexto = await CenarioReconciliacao.ParadoNoEstoqueAsync(mensageria, stringConexao, clienteSem);
        }

        using var coletor = new ColetorTelemetria();
        await using var api = Api(stringConexao, coletor);
        var (cliente, _, _) = await ClienteAsync(api);
        var comContexto = await CenarioReconciliacao.ParadoNoEstoqueAsync(mensageria, stringConexao, cliente);
        var criacao = await CriacaoAsync(coletor, comContexto);
        await CenarioReconciliacao.EnvelhecerAsync(stringConexao, comContexto, UmaHora);
        await CenarioReconciliacao.EnvelhecerAsync(stringConexao, semContexto, UmaHora);

        await using var worker = WorkerRotina(stringConexao, coletor, new() { ["Reconciliacao:Habilitada"] = "true", ["Reconciliacao:IntervaloCicloSegundos"] = "1", ["Reconciliacao:LimiteParadoSegundos"] = "60" });
        worker.CreateClient().Dispose();

        var passo = await coletor.EsperarSpanAsync(s => s.DisplayName == "reconciliar pedido" && Equals(s.Atributo("oms.pedido.id"), comContexto.ToString()), Prazo);
        passo.Should().NotBeNull();
        (passo!.Atributo("oms.resultado"), passo.Atributo("oms.etapa")).Should().Be(("recuperado", "Estoque"));
        passo.Links.Select(l => l.Context.TraceId).Should().Equal([criacao.TraceId], "o passo leva ao trace de criação");
        // O span só é coletado quando termina, e o ciclo termina depois do passo: espera por ele.
        var ciclo = await coletor.EsperarSpanAsync(s => s.SpanId == passo.ParentSpanId, Prazo);
        ciclo.Should().NotBeNull();
        (ciclo!.DisplayName, ciclo.ParentSpanId, ciclo.Atributo("oms.rotina")).Should().Be(("reconciliar", default(ActivitySpanId), "reconciliacao"));

        var publicacao = await coletor.EsperarSpanAsync(s => s.DisplayName == $"outbox publicar {TiposEvento.EstoqueReservado}" && s.ParentSpanId == passo.SpanId, Prazo);
        publicacao.Should().NotBeNull("a recuperação reserva o estoque, e o evento gravado continua o passo");

        var passoSemContexto = await coletor.EsperarSpanAsync(s => s.DisplayName == "reconciliar pedido" && Equals(s.Atributo("oms.pedido.id"), semContexto.ToString()), Prazo);
        passoSemContexto.Should().NotBeNull();
        passoSemContexto!.Links.Should().BeEmpty("o pedido foi criado sem contexto");
        coletor.Soma("oms.rotinas.itens", ("oms.rotina", "reconciliacao"), ("oms.resultado", "recuperado")).Should().Be(2, "métrica 5 (US5)");
    }

    [Fact]
    public async Task Estorno_confirmado_e_um_passo_da_rodada_ligado_ao_pedido()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var coletor = new ColetorTelemetria();
        await using var api = Api(stringConexao, coletor);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidoId = await CenarioEstorno.PedidoPagoAsync(mensageria, stringConexao, cliente);
        var criacao = await CriacaoAsync(coletor, pedidoId);
        await CenarioEstorno.CancelarComoOperacaoAsync(await OperadorAsync(api), pedidoId);

        await using var worker = WorkerRotina(stringConexao, coletor, new() { ["Estornos:Habilitado"] = "true", ["Estornos:IntervaloSegundos"] = "1" });
        worker.CreateClient().Dispose();

        var passo = await coletor.EsperarSpanAsync(s => s.DisplayName == "estornar pedido" && Equals(s.Atributo("oms.pedido.id"), pedidoId.ToString()), Prazo);
        passo.Should().NotBeNull();
        passo!.Atributo("oms.resultado").Should().Be("confirmado");
        passo.Links.Select(l => l.Context.TraceId).Should().Equal(criacao.TraceId);
        // Como na reconciliação: a rodada termina depois do passo.
        var rodada = (await coletor.EsperarSpanAsync(s => s.SpanId == passo.ParentSpanId, Prazo))!;
        (rodada.DisplayName, rodada.ParentSpanId, rodada.Atributo("oms.rotina")).Should().Be(("processar_estornos", default(ActivitySpanId), "estornos"));
        coletor.Soma("oms.rotinas.itens", ("oms.rotina", "estornos"), ("oms.resultado", "confirmado")).Should().Be(1, "métrica 5 (US5)");
    }

    [Fact]
    public async Task Limpeza_mostra_as_linhas_apagadas_por_alvo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            var antigo = DateTimeOffset.UtcNow.AddDays(-10);
            var evento = EventoSaida.De(EnvelopeEvento.Criar(Guid.CreateVersion7(), new PedidoCriado
            {
                PedidoId = Guid.CreateVersion7(),
                Numero = "PED-20261016-000001",
                ClienteId = Guid.CreateVersion7(),
                ValorTotal = 10m,
                Moeda = "BRL",
                Itens = [new ItemPedidoCriado { ProdutoId = Guid.CreateVersion7(), Quantidade = 1, PrecoUnitario = 10m }]
            }, antigo), antigo);
            evento.MarcarEnviado(antigo);
            contexto.EventosSaida.Add(evento);
            await contexto.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var coletor = new ColetorTelemetria();
        await using var worker = WorkerRotina(stringConexao, coletor, []);
        worker.CreateClient().Dispose();

        await worker.Services.GetServices<IHostedService>().OfType<LimpezaMensageria>().Single()
            .ExecutarRodadaAsync(new OpcoesLimpeza(), TestContext.Current.CancellationToken);

        var rodada = coletor.Span(s => s.DisplayName == "limpar");
        rodada.Should().NotBeNull();
        rodada!.ParentSpanId.Should().Be(default(ActivitySpanId));
        var alvos = coletor.Spans.Where(s => s.ParentSpanId == rodada.SpanId && s.DisplayName.StartsWith("limpar ", StringComparison.Ordinal)).ToList();
        alvos.Select(s => s.Atributo("oms.limpeza.alvo")).Should().BeEquivalentTo(["enviados", "processados", "chaves", "pedidos_reenvio"]);
        alvos.Single(s => Equals(s.Atributo("oms.limpeza.alvo"), "enviados")).Atributo("oms.limpeza.linhas_apagadas").Should().Be(1);
        coletor.Soma("oms.limpeza.linhas_apagadas", ("oms.limpeza.alvo", "enviados")).Should().Be(1, "métrica 6 (US5)");
    }

    [Fact]
    public async Task Rodada_sem_o_lock_nao_gera_span()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        using var coletor = new ColetorTelemetria();
        var semPool = new SqlConnectionStringBuilder(stringConexao) { Pooling = false }.ConnectionString;
        await using var conexao = new SqlConnection(semPool);
        await conexao.OpenAsync(TestContext.Current.CancellationToken);
        await using (var comando = new SqlCommand(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;", conexao))
        {
            comando.Parameters.AddWithValue("@recurso", ReconciliadorPedidos.RecursoLock);
            ((int)(await comando.ExecuteScalarAsync(TestContext.Current.CancellationToken))!).Should().BeGreaterThanOrEqualTo(0);
        }

        await using var worker = WorkerRotina(stringConexao, coletor, new() { ["Reconciliacao:Habilitada"] = "true", ["Reconciliacao:IntervaloCicloSegundos"] = "1" });
        worker.CreateClient().Dispose();

        (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(coletor.Spans.Any(s => s.DisplayName == "reconciliar")), TimeSpan.FromSeconds(3)))
            .Should().BeFalse("a instância que não pega o lock não gera trace (FR-011)");
    }

    /// <summary>Worker sem os consumidores, com a rotina dada e a telemetria ligada.</summary>
    private FabricaWorker WorkerRotina(string stringConexao, ColetorTelemetria coletor, Dictionary<string, string?> configuracoes) => new()
    {
        StringConexao = stringConexao,
        ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
        Configuracoes = configuracoes,
        Telemetria = coletor,
        Servicos = colecao =>
        {
            CenarioEstoque.RemoverConsumidor<ReservaEstoque, PedidoCriado>(colecao);
            CenarioEstoque.RemoverConsumidor<ProcessamentoPagamento, EstoqueReservado>(colecao);
            CenarioEstoque.RemoverConsumidor<CriacaoOrdemEntrega, PagamentoAprovado>(colecao);
        }
    };
}
