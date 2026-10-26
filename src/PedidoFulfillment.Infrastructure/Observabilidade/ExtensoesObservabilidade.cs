using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Observabilidade;

/// <summary>
/// Registro da telemetria (REQ-16, R-01, R-08, R-11), chamado pela API e pelo Worker. Só liga o
/// SDK com destino (<c>OTEL_EXPORTER_OTLP_ENDPOINT</c>) ou com <c>Observabilidade:Habilitada</c>
/// (testes); desligado, nada muda além do trace id no console, que só aparece com activity.
/// </summary>
public static class ExtensoesObservabilidade
{
    /// <summary>Fontes nativas do RabbitMQ.Client 7 (R-02); a de conexão fica de fora (ruído).</summary>
    private static readonly string[] FontesRabbitMQ = ["RabbitMQ.Client.Publisher", "RabbitMQ.Client.Subscriber"];

    private static readonly string[] MedidoresNativos =
        ["System.Runtime", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel"];

    /// <summary>Envio das métricas a cada 30 s, se a variável padrão não disser outro (FR-017, analyze I1).</summary>
    private const string ChaveIntervaloMetricas = "OTEL_METRIC_EXPORT_INTERVAL";
    private const string IntervaloMetricasPadraoMs = "30000";

    public static IHostApplicationBuilder AdicionarObservabilidade(this IHostApplicationBuilder builder, string nomeServico)
    {
        var configuracao = builder.Configuration;
        builder.Services.AddSingleton<IValidateOptions<OpcoesObservabilidade>, ValidadorOpcoesObservabilidade>();
        builder.Services.AddOptions<OpcoesObservabilidade>().BindConfiguration(OpcoesObservabilidade.Secao).ValidateOnStart();

        // Trace e span no escopo de cada log (FR-014); o console mostra os escopos.
        builder.Services.Configure<LoggerFactoryOptions>(opcoes =>
            opcoes.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);
        builder.Services.Configure<SimpleConsoleFormatterOptions>(opcoes => opcoes.IncludeScopes = true);

        if (!OpcoesObservabilidade.Ligada(configuracao))
        {
            return builder;
        }

        if (string.IsNullOrWhiteSpace(configuracao[ChaveIntervaloMetricas]))
        {
            builder.Configuration.AddInMemoryCollection([new(ChaveIntervaloMetricas, IntervaloMetricasPadraoMs)]);
        }

        // Taxa inválida derruba a partida pelo validador; aqui só não pode lançar antes dele.
        var taxa = Math.Clamp(configuracao.GetValue($"{OpcoesObservabilidade.Secao}:{nameof(OpcoesObservabilidade.TaxaAmostragem)}", 1.0), 0, 1);
        var telemetria = builder.Services.AddOpenTelemetry()
            // OTEL_SERVICE_NAME sobrescreve o nome padrão (contrato › Recurso).
            .ConfigureResource(recurso => recurso.AddService(configuracao["OTEL_SERVICE_NAME"] is { Length: > 0 } nome ? nome : nomeServico))
            .WithTracing(traces => traces
                .AddSource(TelemetriaOms.Nome)
                .AddSource(FontesRabbitMQ)
                .AddAspNetCoreInstrumentation(opcoes =>
                    // Saúde fica fora (FR-001); corpo e cabeçalhos nunca são gravados (FR-023).
                    opcoes.Filter = contexto => !contexto.Request.Path.StartsWithSegments("/health"))
                // Texto parametrizado sim, valores nunca (clarify 4, FR-023): os parâmetros só
                // entram com uma variável experimental (OTEL_DOTNET_EXPERIMENTAL_SQLCLIENT_*), que o
                // OMS não define e o contrato proíbe.
                .AddSqlClientInstrumentation()
                .SetSampler(new AmostradorSemSqlRaiz(taxa)))
            .WithMetrics(metricas => metricas
                .AddMeter(TelemetriaOms.Nome)
                .AddMeter(MedidoresNativos))
            .WithLogging(
                _ => { },
                opcoes =>
                {
                    opcoes.IncludeFormattedMessage = true;
                    opcoes.IncludeScopes = true;
                });

        // Sem destino (testes), o SDK coleta sem exportar; com destino, exporta em lote (FR-021).
        if (OpcoesObservabilidade.TemDestino(configuracao))
        {
            telemetria.UseOtlpExporter();
        }

        return builder;
    }
}
