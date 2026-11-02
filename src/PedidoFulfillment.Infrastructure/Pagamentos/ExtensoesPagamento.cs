using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Configuracao;
using Polly;

namespace PedidoFulfillment.Infrastructure.Pagamentos;

/// <summary>Registro do gateway de pagamento, usado só pelo Worker (REQ-09, R-02; REQ-19).</summary>
public static class ExtensoesPagamento
{
    /// <summary>Nome do pipeline de resiliência do cliente do gateway.</summary>
    public const string Pipeline = "gateway-pagamento";

    /// <summary>
    /// Gateway por HTTP (REQ-19, research R-06), com as opções validadas na partida (FR-018). O
    /// pipeline, de fora para dentro: tempo total → novas tentativas com espera exponencial e
    /// jitter, respeitando <c>Retry-After</c> → circuito → tempo por tentativa. O
    /// <see cref="MarcadorTentativaHandler"/> fica dentro dele e roda uma vez por tentativa.
    /// </summary>
    public static IServiceCollection AdicionarGatewayPagamento(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<OpcoesGatewayPagamento>, ValidadorOpcoesGatewayPagamento>();
        services.AddOptions<OpcoesGatewayPagamento>().BindConfiguration(OpcoesGatewayPagamento.Secao).ValidateOnStart();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<EstadoGatewayPagamento>();
        services.AddTransient<MarcadorTentativaHandler>();
        var cliente = services.AddHttpClient<IGatewayPagamento, GatewayPagamentoHttp>((provedor, http) =>
            {
                var opcoes = provedor.GetRequiredService<IOptions<OpcoesGatewayPagamento>>().Value;
                http.BaseAddress = new Uri(opcoes.UrlBase!.EndsWith('/') ? opcoes.UrlBase : opcoes.UrlBase + "/");
                http.DefaultRequestHeaders.Add(GatewayPagamentoHttp.CabecalhoChaveApi, opcoes.ChaveApi);
                // O tempo total do pipeline manda; este só não pode cortar antes dele.
                http.Timeout = Timeout.InfiniteTimeSpan;
                provedor.GetRequiredService<EstadoGatewayPagamento>().Iniciar();
            });
        cliente.AddResilienceHandler(Pipeline, (pipeline, contexto) => ConfigurarPipeline(pipeline, contexto.ServiceProvider));
        cliente.AddHttpMessageHandler<MarcadorTentativaHandler>();
        return services;
    }

    private static void ConfigurarPipeline(ResiliencePipelineBuilder<HttpResponseMessage> pipeline, IServiceProvider provedor)
    {
        var opcoes = provedor.GetRequiredService<IOptions<OpcoesGatewayPagamento>>().Value;
        var estado = provedor.GetRequiredService<EstadoGatewayPagamento>();
        var logger = provedor.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(GatewayPagamentoHttp));

        pipeline.AddTimeout(new HttpTimeoutStrategyOptions
        {
            Name = "tempo-total",
            Timeout = TimeSpan.FromMilliseconds(opcoes.TempoTotalMs)
        });

        if (opcoes.Tentativas > 1)
        {
            pipeline.AddRetry(new HttpRetryStrategyOptions
            {
                Name = "novas-tentativas",
                MaxRetryAttempts = opcoes.Tentativas - 1,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(opcoes.EsperaBaseMs),
                ShouldRetryAfterHeader = true,
                ShouldHandle = argumentos => ValueTask.FromResult(ClassificacaoRespostaGateway.Repetir(argumentos.Outcome)),
                OnRetry = argumentos =>
                {
                    var requisicao = argumentos.Context.GetRequestMessage();
                    var pedidoId = requisicao?.Options.TryGetValue(MarcadorTentativaHandler.ChavePedido, out var pedido) == true ? pedido : Guid.Empty;
                    var operacao = requisicao?.Options.TryGetValue(MarcadorTentativaHandler.ChaveOperacao, out var lida) == true ? lida : OperacaoGateway.Cobranca;
                    var tentativa = argumentos.AttemptNumber + 1;
                    if (ClassificacaoRespostaGateway.Incerto(argumentos.Outcome))
                    {
                        EventosGatewayPagamento.ResultadoIncerto(logger, operacao, pedidoId, tentativa);
                    }
                    else
                    {
                        EventosGatewayPagamento.FalhaPassageira(logger, operacao, pedidoId, ClassificacaoRespostaGateway.TipoFalha(argumentos.Outcome), tentativa);
                    }

                    return ValueTask.CompletedTask;
                }
            });
        }

        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            Name = "circuito",
            FailureRatio = opcoes.Circuito.ProporcaoFalhas,
            MinimumThroughput = opcoes.Circuito.MinimoChamadas,
            SamplingDuration = TimeSpan.FromSeconds(opcoes.Circuito.JanelaSegundos),
            BreakDuration = TimeSpan.FromSeconds(opcoes.Circuito.PausaSegundos),
            ShouldHandle = argumentos => ValueTask.FromResult(ClassificacaoRespostaGateway.ContaParaCircuito(argumentos.Outcome)),
            OnOpened = argumentos =>
            {
                var disponivelEm = estado.Abrir(argumentos.BreakDuration);
                EventosGatewayPagamento.CircuitoAberto(logger, argumentos.BreakDuration.TotalSeconds, disponivelEm);
                return ValueTask.CompletedTask;
            },
            OnHalfOpened = _ =>
            {
                estado.MeioAbrir();
                EventosGatewayPagamento.CircuitoMeioAberto(logger);
                return ValueTask.CompletedTask;
            },
            OnClosed = _ =>
            {
                estado.Fechar();
                EventosGatewayPagamento.CircuitoFechado(logger);
                return ValueTask.CompletedTask;
            }
        });

        pipeline.AddTimeout(new HttpTimeoutStrategyOptions
        {
            Name = "tempo-tentativa",
            Timeout = TimeSpan.FromMilliseconds(opcoes.TempoTentativaMs)
        });
    }

    /// <summary>
    /// Rodada dos estornos pendentes, só no Worker (REQ-13, R-04 e R-09), com as opções validadas
    /// na partida. Depende do gateway registrado antes.
    /// </summary>
    public static IServiceCollection AdicionarProcessamentoEstornos(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<OpcoesEstornos>, ValidadorOpcoesEstornos>();
        services.AddOptions<OpcoesEstornos>().BindConfiguration(OpcoesEstornos.Secao).ValidateOnStart();
        services.AddScoped<IConsultaEstornosPendentes, ConsultaEstornosPendentes>();
        services.AddScoped<ServicoEstornos>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<ProcessadorEstornos>();
        return services;
    }
}
