using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Integracao.Entregas;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Observabilidade.CenarioObservabilidade;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Observabilidade;

/// <summary>
/// User Story 1 do REQ-16 (T010): o pedido de ponta a ponta num único trace, pelo outbox e pelo
/// RabbitMQ, com espera no outbox, novas tentativas, DLQ, duplicado e amostragem.
/// </summary>
[Collection(nameof(ColecaoObservabilidade))]
public sealed class TraceFluxoPedidoTestes(MensageriaTeste mensageria)
{
    private const string FonteEntrega = "RabbitMQ.Client.Subscriber";

    [Fact]
    public async Task Pedido_criado_ate_a_entrega_fica_num_unico_trace()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var coletor = new ColetorTelemetria();
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao, coletor);
        var (cliente, _, token) = await ClienteAsync(api);
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, coletor);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        (await CenarioEntrega.EsperarOrdemAsync(stringConexao, pedidoId, Prazo)).Should().BeTrue();
        var entregaEstoque = await EsperarEntregaAsync(coletor, pedidoId, "entrega");
        entregaEstoque.Should().NotBeNull("o consumidor de entrega processa no mesmo trace");
        var requisicao = await CriacaoAsync(coletor, pedidoId);
        requisicao.Should().NotBeNull("a criação gera o span da rota com o pedido");
        requisicao!.Atributo("user.id").Should().Be(Sub(token), "o usuário é o id do token, nunca o e-mail");

        var trace = coletor.DoTrace(requisicao.TraceId);
        foreach (var tipo in new[] { TiposEvento.PedidoCriado, TiposEvento.EstoqueReservado, TiposEvento.PagamentoAprovado })
        {
            trace.Should().Contain(s => s.DisplayName == $"outbox publicar {tipo}" && s.Status != ActivityStatusCode.Error, $"a publicação de {tipo} continua o trace");
        }

        foreach (var consumidor in new[] { "estoque", "pagamento", "entrega" })
        {
            var entrega = trace.SingleOrDefault(s => EhEntrega(s, consumidor));
            entrega.Should().NotBeNull($"o consumo de {consumidor} está no trace");
            entrega!.Atributo("oms.resultado").Should().Be("processado");
            entrega.Atributo("oms.pedido.id").Should().Be(pedidoId.ToString());
            entrega.Atributo("messaging.message.id").Should().NotBeNull();
        }

        // O evento gravado por um consumidor é publicado como descendente do processamento que o gravou.
        var porId = trace.ToDictionary(s => s.SpanId);
        var publicacaoReserva = trace.Single(s => s.DisplayName == $"outbox publicar {TiposEvento.EstoqueReservado}");
        Ancestrais(publicacaoReserva, porId).Should().Contain(s => EhEntrega(s, "estoque"));
        var publicacaoPagamento = trace.Single(s => s.DisplayName == $"outbox publicar {TiposEvento.PagamentoAprovado}");
        Ancestrais(publicacaoPagamento, porId).Should().Contain(s => EhEntrega(s, "pagamento"));
    }

    [Fact]
    public async Task Broker_fora_do_ar_mostra_a_espera_no_outbox_no_mesmo_trace()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var coletor = new ColetorTelemetria();
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao, coletor);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, coletor);

        await mensageria.PausarBrokerAsync();
        Guid pedidoId;
        try
        {
            pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
            await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        }
        finally
        {
            await mensageria.RetomarBrokerAsync();
        }

        var consumo = await EsperarEntregaAsync(coletor, pedidoId, "estoque");
        consumo.Should().NotBeNull();
        var requisicao = await CriacaoAsync(coletor, pedidoId);
        consumo!.TraceId.Should().Be(requisicao.TraceId, "a publicação e o consumo continuam o trace da requisição");
        coletor.DoTrace(requisicao.TraceId)
            .Should().Contain(s => s.DisplayName == $"outbox publicar {TiposEvento.PedidoCriado}" && s.Status != ActivityStatusCode.Error);
        // O intervalo aparece no trace: na espera antes de publicar (oms.outbox.espera_ms) ou na
        // publicação esperando a confirmação do broker pausado.
        (consumo.StartTimeUtc - (requisicao.StartTimeUtc + requisicao.Duration)).Should().BeGreaterThanOrEqualTo(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Nova_tentativa_dlq_e_duplicado_ficam_no_trace_do_pedido()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var coletor = new ColetorTelemetria();
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao, coletor);
        var (cliente, _, _) = await ClienteAsync(api);
        await mensageria.LimparFilasConsumidorAsync(ManipuladorFalhaControlada.Nome);
        var controle = new ControleFalhaControlada();
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, coletor, servicos: servicos =>
        {
            servicos.AddSingleton(controle);
            servicos.AdicionarConsumidor<ManipuladorFalhaControlada, PedidoCriado>();
        });
        (await mensageria.ConsumidorLigadoAsync(ManipuladorFalhaControlada.Nome, TimeSpan.FromSeconds(10))).Should().BeTrue();

        // Falha uma vez e acerta na nova tentativa.
        controle.FalharVezes = 1;
        var umaFalha = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        var processado = await coletor.EsperarSpanAsync(s => EhEntrega(s, ManipuladorFalhaControlada.Nome, umaFalha, "processado"), Prazo);
        processado.Should().NotBeNull();
        var tentativas = coletor.DoTrace(processado!.TraceId).Where(s => EhEntrega(s, ManipuladorFalhaControlada.Nome)).ToList();
        tentativas.Should().HaveCount(2, "a nova tentativa fica no mesmo trace");
        tentativas[0].Atributo("oms.resultado").Should().Be("nova_tentativa");
        tentativas[0].Status.Should().Be(ActivityStatusCode.Error);
        tentativas[0].Atributo("error.type").Should().Be(nameof(InvalidOperationException));

        // Falha sempre: a última vai para a DLQ, com erro.
        controle.FalharVezes = int.MaxValue;
        var semprefalha = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        var dlq = await coletor.EsperarSpanAsync(s => EhEntrega(s, ManipuladorFalhaControlada.Nome, semprefalha, "dlq"), Prazo);
        dlq.Should().NotBeNull("a ida para a DLQ fica no trace do pedido");
        dlq!.Status.Should().Be(ActivityStatusCode.Error);
        coletor.DoTrace(dlq.TraceId).Should().Contain(s => s.EhRequisicao("POST", "/api/v1/pedidos/"));

        // Mensagem repetida: duplicado, sem erro.
        await CenarioEntrega.RepublicarPagamentoAprovadoAsync(mensageria, stringConexao, umaFalha);
        var duplicado = await coletor.EsperarSpanAsync(s => EhEntrega(s, "entrega", umaFalha, "duplicado"), Prazo);
        duplicado.Should().NotBeNull();
        duplicado!.Status.Should().NotBe(ActivityStatusCode.Error);

        // Métrica 4 (US5): por consumidor e resultado.
        coletor.Soma("oms.mensageria.mensagens", ("oms.consumidor", ManipuladorFalhaControlada.Nome), ("oms.resultado", "nova_tentativa")).Should().Be(4);
        coletor.Soma("oms.mensageria.mensagens", ("oms.consumidor", ManipuladorFalhaControlada.Nome), ("oms.resultado", "dlq")).Should().Be(1);
        coletor.Soma("oms.mensageria.mensagens", ("oms.consumidor", ManipuladorFalhaControlada.Nome), ("oms.resultado", "processado")).Should().Be(1);
        coletor.Soma("oms.mensageria.mensagens", ("oms.consumidor", "entrega"), ("oms.resultado", "duplicado")).Should().Be(1);
    }

    [Fact]
    public async Task Pedido_nao_amostrado_na_criacao_nao_gera_span_em_nenhum_processo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var coletor = new ColetorTelemetria();
        var stringConexao = mensageria.NovoBanco();
        var semAmostragem = new Dictionary<string, string?> { ["Observabilidade:TaxaAmostragem"] = "0" };
        await using var api = Api(stringConexao, coletor, semAmostragem);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, coletor, semAmostragem);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        (await CenarioEntrega.EsperarOrdemAsync(stringConexao, pedidoId, Prazo)).Should().BeTrue("o fluxo termina normalmente");
        coletor.Spans.Should().NotContain(s => Equals(s.Atributo("oms.pedido.id"), pedidoId.ToString()), "FR-022");
        coletor.Spans.Should().NotContain(s => s.DisplayName.StartsWith("outbox publicar", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Contexto_invalido_no_outbox_e_ignorado_e_o_evento_sai_num_trace_novo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var coletor = new ColetorTelemetria();
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        var idEvento = Guid.CreateVersion7();
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            contexto.EventosSaida.Add(EventoSaida.De(new EnvelopeEvento<PedidoCancelado>
            {
                IdEvento = idEvento,
                Tipo = TiposEvento.PedidoCancelado,
                Versao = TiposEvento.VersaoAtual,
                OcorridoEm = DateTimeOffset.UtcNow,
                IdCorrelacao = Guid.CreateVersion7(),
                Dados = new PedidoCancelado
                {
                    PedidoId = Guid.CreateVersion7(),
                    Numero = "PED-20261026-000001",
                    Motivo = MotivoCancelamento.Cliente,
                    EtapaEsgotada = null,
                    CanceladoEm = DateTimeOffset.UtcNow,
                    EstoqueDevolvido = false,
                    EstornoPendente = false,
                    OrdemEntregaCancelada = false
                }
            }, DateTimeOffset.UtcNow));
            await contexto.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await BancoDadosTeste.ExecutarAsync(stringConexao, $"UPDATE EventosSaida SET ContextoRastreio = '00-zz' WHERE IdEvento = '{idEvento}'");
        await using var worker = await WorkerProntoAsync(mensageria, stringConexao, coletor);

        var publicacao = await coletor.EsperarSpanAsync(s => Equals(s.Atributo("messaging.message.id"), idEvento.ToString()) && s.Source.Name == "PedidoFulfillment", Prazo);
        publicacao.Should().NotBeNull("o evento é publicado normalmente (FR-013)");
        publicacao!.ParentSpanId.Should().Be(default(ActivitySpanId), "sem contexto válido, começa um trace novo");
        publicacao.Status.Should().NotBe(ActivityStatusCode.Error);
    }

    private static bool EhEntrega(Activity span, string consumidor) =>
        span.Source.Name == FonteEntrega && span.Kind == ActivityKind.Consumer && Equals(span.Atributo("oms.consumidor"), consumidor);

    private static bool EhEntrega(Activity span, string consumidor, Guid pedidoId, string resultado) =>
        EhEntrega(span, consumidor) && Equals(span.Atributo("oms.pedido.id"), pedidoId.ToString()) && Equals(span.Atributo("oms.resultado"), resultado);

    private static Task<Activity?> EsperarEntregaAsync(ColetorTelemetria coletor, Guid pedidoId, string consumidor) =>
        coletor.EsperarSpanAsync(s => EhEntrega(s, consumidor, pedidoId, "processado"), Prazo);

    private static IEnumerable<Activity> Ancestrais(Activity span, IReadOnlyDictionary<ActivitySpanId, Activity> porId)
    {
        var atual = span;
        while (porId.TryGetValue(atual.ParentSpanId, out var pai))
        {
            yield return pai;
            atual = pai;
        }
    }

    /// <summary>O <c>sub</c> do token (payload em base64url).</summary>
    private static string Sub(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        return json.RootElement.GetProperty("sub").GetString()!;
    }
}

public sealed class ControleFalhaControlada
{
    /// <summary>Quantas vezes cada evento falha antes de processar (<see cref="int.MaxValue"/> = sempre).</summary>
    public int FalharVezes { get; set; }

    public System.Collections.Concurrent.ConcurrentDictionary<Guid, int> Execucoes { get; } = new();
}

/// <summary>Consumidor de <c>pedido.criado</c> que falha sob controle, só para os testes de telemetria.</summary>
public sealed class ManipuladorFalhaControlada(ControleFalhaControlada controle) : IManipuladorEvento<PedidoCriado>
{
    public const string Nome = "observabilidade";

    public string Consumidor => Nome;

    public IReadOnlyCollection<string> Tipos => [TiposEvento.PedidoCriado];

    public Task ManipularAsync(EnvelopeEvento<PedidoCriado> evento, CancellationToken cancellationToken)
    {
        var execucao = controle.Execucoes.AddOrUpdate(evento.IdEvento, 1, (_, anterior) => anterior + 1);
        return execucao <= controle.FalharVezes
            ? throw new InvalidOperationException("falha controlada de teste")
            : Task.CompletedTask;
    }
}
