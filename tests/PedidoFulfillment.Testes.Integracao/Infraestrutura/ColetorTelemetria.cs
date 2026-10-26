using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Coleta em memória a telemetria da API e do Worker (REQ-16, R-12): spans e logs pelos
/// processadores do próprio SDK (o amostrador do OMS é exercitado) e métricas pelo
/// <see cref="MeterListener"/> da BCL. Sem o pacote <c>OpenTelemetry.Exporter.InMemory</c>.
/// Como a API e o Worker rodam no mesmo processo, as consultas filtram pelo trace id.
/// </summary>
public sealed class ColetorTelemetria : IDisposable
{
    private const string Medidor = "PedidoFulfillment";

    // As fontes valem para o processo inteiro: os provedores da API e do Worker recebem os mesmos
    // spans. Guardados pelo id do span, sem repetição.
    private readonly ConcurrentDictionary<ActivitySpanId, Activity> spans = new();
    private readonly ConcurrentQueue<LogColetado> logs = new();
    private readonly ConcurrentQueue<MedicaoColetada> medicoes = new();
    private readonly MeterListener ouvinte = new();

    public ColetorTelemetria()
    {
        ouvinte.InstrumentPublished = (instrumento, listener) =>
        {
            if (instrumento.Meter.Name == Medidor)
            {
                listener.EnableMeasurementEvents(instrumento);
            }
        };
        ouvinte.SetMeasurementEventCallback<long>((instrumento, valor, atributos, _) => Registrar(instrumento, valor, atributos));
        ouvinte.SetMeasurementEventCallback<double>((instrumento, valor, atributos, _) => Registrar(instrumento, valor, atributos));
        ouvinte.Start();
    }

    public IReadOnlyCollection<Activity> Spans => [.. spans.Values];

    public IReadOnlyCollection<LogColetado> Logs => logs;

    public IReadOnlyCollection<MedicaoColetada> Medicoes => medicoes;

    /// <summary>Liga a coleta numa fábrica: um processador novo por provedor (API e Worker).</summary>
    public void RegistrarEm(IServiceCollection servicos)
    {
        servicos.ConfigureOpenTelemetryTracerProvider(traces => traces.AddProcessor(new ProcessadorSpans(spans)));
        servicos.ConfigureOpenTelemetryLoggerProvider(registros => registros.AddProcessor(new ProcessadorLogs(logs)));
    }

    /// <summary>Os spans do trace, na ordem de início.</summary>
    public IReadOnlyList<Activity> DoTrace(ActivityTraceId traceId) =>
        [.. spans.Values.Where(s => s.TraceId == traceId).OrderBy(s => s.StartTimeUtc)];

    /// <summary>O primeiro span terminado que atende, ou nulo.</summary>
    public Activity? Span(Func<Activity, bool> condicao) => spans.Values.FirstOrDefault(condicao);

    /// <summary>Espera um span que atenda à condição, até o prazo.</summary>
    public async Task<Activity?> EsperarSpanAsync(Func<Activity, bool> condicao, TimeSpan prazo)
    {
        await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(spans.Values.Any(condicao)), prazo);
        return Span(condicao);
    }

    /// <summary>Lê agora os gauges observáveis (o exportador faria isso no intervalo dele).</summary>
    public void LerGauges() => ouvinte.RecordObservableInstruments();

    /// <summary>Soma das medições de um contador com os atributos dados.</summary>
    public double Soma(string metrica, params (string Chave, string Valor)[] atributos) =>
        medicoes.Where(m => m.Nome == metrica && Atende(m, atributos)).Sum(m => m.Valor);

    /// <summary>A última leitura de um gauge com os atributos dados, ou nulo.</summary>
    public double? Ultimo(string metrica, params (string Chave, string Valor)[] atributos) =>
        medicoes.LastOrDefault(m => m.Nome == metrica && Atende(m, atributos))?.Valor;

    public void Dispose() => ouvinte.Dispose();

    private static bool Atende(MedicaoColetada medicao, (string Chave, string Valor)[] atributos) =>
        atributos.All(a => medicao.Atributos.TryGetValue(a.Chave, out var valor) && valor == a.Valor);

    private void Registrar(Instrument instrumento, double valor, ReadOnlySpan<KeyValuePair<string, object?>> atributos)
    {
        var copia = new Dictionary<string, string?>();
        foreach (var atributo in atributos)
        {
            copia[atributo.Key] = atributo.Value?.ToString();
        }

        medicoes.Enqueue(new MedicaoColetada(instrumento.Name, instrumento.Unit, valor, copia));
    }

    private sealed class ProcessadorSpans(ConcurrentDictionary<ActivitySpanId, Activity> destino) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => destino.TryAdd(data.SpanId, data);
    }

    /// <summary>O <see cref="LogRecord"/> é reaproveitado pelo SDK: copia o que interessa.</summary>
    private sealed class ProcessadorLogs(ConcurrentQueue<LogColetado> destino) : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data)
        {
            var atributos = new Dictionary<string, string?>();
            foreach (var atributo in data.Attributes ?? [])
            {
                atributos[atributo.Key] = atributo.Value?.ToString();
            }

            destino.Enqueue(new LogColetado(
                data.CategoryName, data.EventId, data.LogLevel, data.FormattedMessage, data.TraceId, data.SpanId, atributos));
        }
    }
}

public sealed record LogColetado(
    string? Categoria, EventId Evento, LogLevel Nivel, string? Mensagem, ActivityTraceId TraceId, ActivitySpanId SpanId,
    IReadOnlyDictionary<string, string?> Atributos);

public sealed record MedicaoColetada(string Nome, string? Unidade, double Valor, IReadOnlyDictionary<string, string?> Atributos);
