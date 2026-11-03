using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Pagamentos;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;

namespace PedidoFulfillment.Testes.Unitarios.Pagamentos;

/// <summary>
/// O cliente do gateway com o pipeline de resiliência de verdade e um handler falso no lugar da
/// rede (REQ-19, FR-011 a FR-014 e FR-016, research R-06 e R-07), com os tempos curtos dos testes.
/// </summary>
public sealed class PipelineGatewayPagamentoTestes : IDisposable
{
    private const string ChaveApi = "chave-api-de-teste-123";
    private static readonly Guid Pedido = Guid.Parse("0199f6b1-2c3d-7e4f-8a5b-6c7d8e9f0a12");

    private readonly HandlerRoteirizado rede = new();
    private readonly ProvedorLogsFalso logs = new();
    private ServiceProvider? provedor;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => provedor?.Dispose();

    private IGatewayPagamento Gateway(Dictionary<string, string?>? ajustes = null)
    {
        var configuracao = new Dictionary<string, string?>
        {
            ["GatewayPagamento:UrlBase"] = "http://pagamentos.teste",
            ["GatewayPagamento:ChaveApi"] = ChaveApi,
            ["GatewayPagamento:TempoTentativaMs"] = "200",
            ["GatewayPagamento:Tentativas"] = "3",
            ["GatewayPagamento:EsperaBaseMs"] = "10",
            ["GatewayPagamento:TempoTotalMs"] = "2500",
            ["GatewayPagamento:Circuito:MinimoChamadas"] = "100",
            ["GatewayPagamento:Circuito:JanelaSegundos"] = "30",
            ["GatewayPagamento:Circuito:PausaSegundos"] = "30"
        };
        foreach (var (chave, valor) in ajustes ?? [])
        {
            configuracao[chave] = valor;
        }

        var servicos = new ServiceCollection();
        servicos.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuracao).Build());
        servicos.AddLogging(l => l.AddProvider(logs).SetMinimumLevel(LogLevel.Debug));
        servicos.AdicionarGatewayPagamento();
        servicos.AddHttpClient<IGatewayPagamento, GatewayPagamentoHttp>().ConfigurePrimaryHttpMessageHandler(() => rede);
        provedor = servicos.BuildServiceProvider();
        return provedor.GetRequiredService<IGatewayPagamento>();
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string corpo, TimeSpan? retryAfter = null)
    {
        var resposta = new HttpResponseMessage(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        if (retryAfter is { } espera)
        {
            resposta.Headers.RetryAfter = new RetryConditionHeaderValue(espera);
        }

        return resposta;
    }

    private static HttpResponseMessage Aprovada() =>
        Json(HttpStatusCode.Created, "{\"situacao\":\"aprovada\",\"identificador\":\"PAG-1\",\"valor\":10}");

    private static HttpResponseMessage Problema(HttpStatusCode status, string codigo, TimeSpan? retryAfter = null) =>
        Json(status, $"{{\"title\":\"x\",\"status\":{(int)status},\"codigo\":\"{codigo}\"}}", retryAfter);

    [Fact]
    public async Task Envia_a_chave_do_OMS_e_o_pedido_como_Idempotency_Key()
    {
        rede.Enfileirar(Aprovada);

        var resultado = await Gateway().CobrarAsync(Pedido, 149.70m, Token);

        resultado.Should().Be(ResultadoCobranca.Aprovada("PAG-1"));
        var requisicao = rede.Recebidas.Should().ContainSingle().Subject;
        requisicao.Url.Should().Be("http://pagamentos.teste/v1/cobrancas");
        requisicao.Cabecalhos["X-Api-Key"].Should().Be(ChaveApi);
        requisicao.Cabecalhos["Idempotency-Key"].Should().Be(Pedido.ToString());
        requisicao.Corpo.Should().Be("{\"valor\":149.70}");
    }

    [Fact]
    public async Task Falha_passageira_e_repetida_com_a_mesma_chave_e_registrada_em_log()
    {
        rede.Enfileirar(() => Problema(HttpStatusCode.ServiceUnavailable, "indisponivel"), Aprovada);

        var resultado = await Gateway().CobrarAsync(Pedido, 10m, Token);

        resultado.Aprovado.Should().BeTrue();
        rede.Recebidas.Should().HaveCount(2).And.OnlyContain(r => r.Cabecalhos["Idempotency-Key"] == Pedido.ToString());
        var log = logs.Eventos.Should().ContainSingle(e => e.Id.Id == 7013).Subject;
        (log.Propriedades["TipoFalha"], log.Propriedades["Tentativa"]).Should().Be(("indisponivel", 1));
        log.Mensagem.Should().NotContain(ChaveApi).And.NotContain("PAG-1");
    }

    [Fact]
    public async Task Limite_de_taxa_espera_pelo_menos_o_Retry_After()
    {
        rede.Enfileirar(() => Problema(HttpStatusCode.TooManyRequests, "limite-taxa", TimeSpan.FromMilliseconds(600)), Aprovada);
        var gateway = Gateway();
        var cronometro = Stopwatch.StartNew();

        (await gateway.CobrarAsync(Pedido, 10m, Token)).Aprovado.Should().BeTrue();

        cronometro.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(550));
    }

    [Fact]
    public async Task Operacao_em_andamento_e_repetida()
    {
        rede.Enfileirar(() => Problema(HttpStatusCode.Conflict, "operacao-em-andamento", TimeSpan.FromMilliseconds(50)), Aprovada);

        (await Gateway().CobrarAsync(Pedido, 10m, Token)).Aprovado.Should().BeTrue();

        logs.Eventos.Should().ContainSingle(e => e.Id.Id == 7013).Which.Propriedades["TipoFalha"].Should().Be("em-andamento");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "requisicao-invalida")]
    [InlineData(HttpStatusCode.Unauthorized, "credencial-recusada")]
    [InlineData(HttpStatusCode.Conflict, "chave-reutilizada")]
    public async Task Erro_permanente_nao_e_repetido_e_vai_para_o_log_como_erro(HttpStatusCode status, string codigo)
    {
        rede.Enfileirar(() => status == HttpStatusCode.Unauthorized
            ? Json(status, "{\"title\":\"Credencial inválida.\",\"status\":401}")
            : Problema(status, codigo));

        var cobrar = () => Gateway().CobrarAsync(Pedido, 10m, Token);

        (await cobrar.Should().ThrowAsync<FalhaGatewayPagamentoException>()).Which.Tipo.Should().Be(TipoFalhaGateway.Permanente);
        rede.Recebidas.Should().ContainSingle();
        var log = logs.Eventos.Should().ContainSingle(e => e.Id.Id == 7015).Subject;
        log.Nivel.Should().Be(LogLevel.Error);
        (log.Propriedades["StatusHttp"], log.Propriedades["Codigo"]).Should().Be(((int)status, codigo));
    }

    [Fact]
    public async Task Corpo_fora_do_contrato_e_erro_permanente()
    {
        rede.Enfileirar(() => Json(HttpStatusCode.Created, "{\"situacao\":\"pendente\"}"));

        var cobrar = () => Gateway().CobrarAsync(Pedido, 10m, Token);

        (await cobrar.Should().ThrowAsync<FalhaGatewayPagamentoException>()).Which.Tipo.Should().Be(TipoFalhaGateway.Permanente);
        logs.Eventos.Should().ContainSingle(e => e.Id.Id == 7015).Which.Propriedades["Codigo"].Should().Be("corpo-fora-do-contrato");
    }

    [Fact]
    public async Task Esgotadas_as_tentativas_a_falha_e_passageira_sem_resultado()
    {
        rede.Enfileirar(() => Problema(HttpStatusCode.InternalServerError, "erro-interno"), () => Problema(HttpStatusCode.InternalServerError, "erro-interno"),
            () => Problema(HttpStatusCode.InternalServerError, "erro-interno"));

        var cobrar = () => Gateway().CobrarAsync(Pedido, 10m, Token);

        (await cobrar.Should().ThrowAsync<FalhaGatewayPagamentoException>()).Which.Tipo.Should().Be(TipoFalhaGateway.Passageira);
        rede.Recebidas.Should().HaveCount(3, "até Tentativas, contando a primeira");
        logs.Eventos.Count(e => e.Id.Id == 7013).Should().Be(2);
    }

    [Fact]
    public async Task Tempo_da_tentativa_esgotado_e_resultado_incerto_e_a_repeticao_usa_a_mesma_chave()
    {
        rede.Enfileirar(async token =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return Aprovada();
        }, _ => Task.FromResult(Aprovada()));

        var resultado = await Gateway().CobrarAsync(Pedido, 10m, Token);

        resultado.Aprovado.Should().BeTrue();
        rede.Recebidas.Should().HaveCount(2).And.OnlyContain(r => r.Cabecalhos["Idempotency-Key"] == Pedido.ToString());
        logs.Eventos.Should().ContainSingle(e => e.Id.Id == 7014).Which.Propriedades["Tentativa"].Should().Be(1);
    }

    [Fact]
    public async Task O_tempo_total_limita_a_chamada()
    {
        rede.Sempre(async token =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            return Aprovada();
        });
        var gateway = Gateway(new() { ["GatewayPagamento:TempoTentativaMs"] = "300", ["GatewayPagamento:TempoTotalMs"] = "500" });
        var cronometro = Stopwatch.StartNew();

        var cobrar = () => gateway.CobrarAsync(Pedido, 10m, Token);

        (await cobrar.Should().ThrowAsync<FalhaGatewayPagamentoException>()).Which.Tipo.Should().Be(TipoFalhaGateway.Passageira);
        cronometro.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "FR-019: o tempo total corta as tentativas");
    }

    [Fact]
    public async Task Falha_de_conexao_e_passageira_e_repetida()
    {
        rede.Enfileirar(_ => throw new HttpRequestException("conexão recusada"), _ => Task.FromResult(Aprovada()));

        (await Gateway().CobrarAsync(Pedido, 10m, Token)).Aprovado.Should().BeTrue();

        logs.Eventos.Should().ContainSingle(e => e.Id.Id == 7013).Which.Propriedades["TipoFalha"].Should().Be("conexao");
    }

    [Fact]
    public async Task UrlBase_com_barra_no_fim_monta_a_mesma_rota()
    {
        rede.Enfileirar(Aprovada);

        await Gateway(new() { ["GatewayPagamento:UrlBase"] = "http://pagamentos.teste/" }).CobrarAsync(Pedido, 10m, Token);

        rede.Recebidas.Should().ContainSingle().Which.Url.Should().Be("http://pagamentos.teste/v1/cobrancas");
    }

    [Fact]
    public async Task Falha_de_conexao_em_todas_as_tentativas_e_passageira()
    {
        rede.Sempre(_ => throw new HttpRequestException("conexão recusada"));

        var cobrar = () => Gateway().CobrarAsync(Pedido, 10m, Token);

        var falha = (await cobrar.Should().ThrowAsync<FalhaGatewayPagamentoException>()).Which;
        falha.Tipo.Should().Be(TipoFalhaGateway.Passageira);
        falha.InnerException.Should().BeOfType<HttpRequestException>();
        rede.Recebidas.Should().HaveCount(3);
    }

    [Fact]
    public async Task Corpo_que_nao_e_json_e_erro_permanente()
    {
        rede.Enfileirar(() => Json(HttpStatusCode.Created, "não é json"));

        var cobrar = () => Gateway().CobrarAsync(Pedido, 10m, Token);

        (await cobrar.Should().ThrowAsync<FalhaGatewayPagamentoException>()).Which.Tipo.Should().Be(TipoFalhaGateway.Permanente);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "texto solto", "sem-codigo")]
    [InlineData(HttpStatusCode.Unauthorized, "texto solto", "credencial-recusada")]
    [InlineData(HttpStatusCode.Forbidden, "{\"title\":\"x\"}", "sem-codigo")]
    public async Task Erro_permanente_sem_codigo_legivel_usa_o_codigo_do_status(HttpStatusCode status, string corpo, string codigo)
    {
        rede.Enfileirar(() => new HttpResponseMessage(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/problem+json") });

        var cobrar = () => Gateway().CobrarAsync(Pedido, 10m, Token);

        await cobrar.Should().ThrowAsync<FalhaGatewayPagamentoException>();
        logs.Eventos.Should().ContainSingle(e => e.Id.Id == 7015).Which.Propriedades["Codigo"].Should().Be(codigo);
    }

    [Fact]
    public async Task Consulta_recusada_e_estorno_recusado_sao_resultados()
    {
        rede.Enfileirar(() => Json(HttpStatusCode.OK, "{\"situacao\":\"recusada\",\"motivo\":\"Pagamento recusado pelo emissor.\"}"),
            () => Json(HttpStatusCode.Created, "{\"situacao\":\"recusado\",\"motivo\":\"Cobrança desconhecida.\"}"));
        var gateway = Gateway();

        (await gateway.ConsultarCobrancaAsync(Pedido, Token)).Situacao.Should().Be(SituacaoConsultaCobranca.Recusada);
        (await gateway.EstornarAsync(Pedido, "PAG-1", 10m, Token)).Should().Be(ResultadoEstorno.Recusar("Cobrança desconhecida."));
    }

    [Fact]
    public async Task Recusa_de_negocio_nao_e_repetida()
    {
        rede.Enfileirar(() => Json(HttpStatusCode.Created, "{\"situacao\":\"recusada\",\"motivo\":\"Pagamento recusado pelo emissor.\"}"));

        (await Gateway().CobrarAsync(Pedido, 10m, Token)).Should().Be(ResultadoCobranca.Recusada("Pagamento recusado pelo emissor."));

        rede.Recebidas.Should().ContainSingle();
    }

    [Fact]
    public async Task Circuito_abre_depois_das_falhas_e_a_chamada_seguinte_nem_sai()
    {
        rede.Sempre(_ => Task.FromResult(Problema(HttpStatusCode.ServiceUnavailable, "indisponivel")));
        var gateway = Gateway(new()
        {
            ["GatewayPagamento:Tentativas"] = "1",
            ["GatewayPagamento:Circuito:MinimoChamadas"] = "2",
            ["GatewayPagamento:Circuito:PausaSegundos"] = "30"
        });

        for (var i = 0; i < 2; i++)
        {
            var falhar = () => gateway.CobrarAsync(Guid.CreateVersion7(), 10m, Token);
            await falhar.Should().ThrowAsync<FalhaGatewayPagamentoException>();
        }

        var bloqueada = () => gateway.CobrarAsync(Pedido, 10m, Token);

        var excecao = (await bloqueada.Should().ThrowAsync<GatewayPagamentoIndisponivelException>()).Which;
        excecao.DisponivelEm.Should().BeAfter(DateTimeOffset.UtcNow.AddSeconds(20));
        rede.Recebidas.Should().HaveCount(2, "SC-004: com o circuito aberto, nenhuma chamada sai");
        logs.Eventos.Should().ContainSingle(e => e.Id.Id == 7016);
    }

    [Fact]
    public async Task Recusas_de_negocio_e_erros_permanentes_nao_abrem_o_circuito()
    {
        rede.Sempre(_ => Task.FromResult(Json(HttpStatusCode.Created, "{\"situacao\":\"recusada\",\"motivo\":\"x\"}")));
        var gateway = Gateway(new() { ["GatewayPagamento:Circuito:MinimoChamadas"] = "2" });

        for (var i = 0; i < 5; i++)
        {
            (await gateway.CobrarAsync(Guid.CreateVersion7(), 10m, Token)).Aprovado.Should().BeFalse();
        }

        logs.Eventos.Should().NotContain(e => e.Id.Id == 7016);
    }

    [Fact]
    public async Task Passada_a_pausa_a_chamada_de_teste_fecha_o_circuito()
    {
        var falhar = true;
        rede.Sempre(_ => Task.FromResult(falhar ? Problema(HttpStatusCode.ServiceUnavailable, "indisponivel") : Aprovada()));
        var gateway = Gateway(new()
        {
            ["GatewayPagamento:Tentativas"] = "1",
            ["GatewayPagamento:Circuito:MinimoChamadas"] = "2",
            ["GatewayPagamento:Circuito:PausaSegundos"] = "1"
        });
        for (var i = 0; i < 2; i++)
        {
            var cobrar = () => gateway.CobrarAsync(Guid.CreateVersion7(), 10m, Token);
            await cobrar.Should().ThrowAsync<FalhaGatewayPagamentoException>();
        }

        falhar = false;
        await Task.Delay(TimeSpan.FromMilliseconds(1100), Token);

        (await gateway.CobrarAsync(Pedido, 10m, Token)).Aprovado.Should().BeTrue();
        logs.Eventos.Select(e => e.Id.Id).Should().ContainInOrder(7016, 7017, 7018);
    }

    [Fact]
    public async Task Consulta_404_e_nao_encontrada_e_200_traz_a_cobranca()
    {
        rede.Enfileirar(() => Problema(HttpStatusCode.NotFound, "nao-encontrada"),
            () => Json(HttpStatusCode.OK, "{\"situacao\":\"aprovada\",\"identificador\":\"PAG-9\"}"));
        var gateway = Gateway();

        (await gateway.ConsultarCobrancaAsync(Pedido, Token)).Should().Be(ConsultaCobranca.NaoEncontrada);
        (await gateway.ConsultarCobrancaAsync(Pedido, Token)).Should().Be(ConsultaCobranca.Aprovada("PAG-9"));

        rede.Recebidas.Should().OnlyContain(r => r.Url == $"http://pagamentos.teste/v1/cobrancas/{Pedido}" && !r.Cabecalhos.ContainsKey("Idempotency-Key"));
    }

    [Fact]
    public async Task Estorno_envia_a_transacao_e_le_a_confirmacao()
    {
        rede.Enfileirar(() => Json(HttpStatusCode.Created, "{\"situacao\":\"confirmado\",\"identificador\":\"EST-1\"}"));

        (await Gateway().EstornarAsync(Pedido, "PAG-1", 10m, Token)).Should().Be(ResultadoEstorno.Confirmar("EST-1"));

        var requisicao = rede.Recebidas.Should().ContainSingle().Subject;
        requisicao.Url.Should().Be("http://pagamentos.teste/v1/estornos");
        requisicao.Corpo.Should().Be("{\"identificadorCobranca\":\"PAG-1\",\"valor\":10}");
    }

    /// <summary>Uma requisição recebida pelo handler falso.</summary>
    private sealed record Recebida(string Url, IReadOnlyDictionary<string, string> Cabecalhos, string Corpo);

    /// <summary>Responde em ordem as respostas enfileiradas; depois, a padrão.</summary>
    private sealed class HandlerRoteirizado : HttpMessageHandler
    {
        private readonly ConcurrentQueue<Func<CancellationToken, Task<HttpResponseMessage>>> roteiro = new();
        private readonly ConcurrentQueue<Recebida> recebidas = new();
        private Func<CancellationToken, Task<HttpResponseMessage>> padrao = _ => throw new InvalidOperationException("Sem resposta programada.");

        public IReadOnlyCollection<Recebida> Recebidas => [.. recebidas];

        public void Enfileirar(params Func<HttpResponseMessage>[] respostas)
        {
            foreach (var resposta in respostas)
            {
                roteiro.Enqueue(_ => Task.FromResult(resposta()));
            }
        }

        public void Enfileirar(params Func<CancellationToken, Task<HttpResponseMessage>>[] respostas)
        {
            foreach (var resposta in respostas)
            {
                roteiro.Enqueue(resposta);
            }
        }

        public void Sempre(Func<CancellationToken, Task<HttpResponseMessage>> resposta) => padrao = resposta;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var cabecalhos = request.Headers.ToDictionary(c => c.Key, c => string.Join(",", c.Value), StringComparer.OrdinalIgnoreCase);
            var corpo = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            recebidas.Enqueue(new Recebida(request.RequestUri!.ToString(), cabecalhos, corpo));
            var resposta = roteiro.TryDequeue(out var proxima) ? proxima : padrao;
            return await resposta(cancellationToken);
        }
    }

    /// <summary>Guarda os logs de todos os loggers criados pela fábrica.</summary>
    private sealed class ProvedorLogsFalso : ILoggerProvider
    {
        private readonly ConcurrentQueue<EventoLog> eventos = new();

        public IReadOnlyCollection<EventoLog> Eventos => [.. eventos];

        public ILogger CreateLogger(string categoryName) => new Logger(eventos);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<EventoLog> eventos) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var propriedades = (state as IEnumerable<KeyValuePair<string, object?>> ?? []).ToDictionary(p => p.Key, p => p.Value);
                eventos.Enqueue(new EventoLog(eventId, logLevel, formatter(state, exception), propriedades));
            }
        }
    }
}
