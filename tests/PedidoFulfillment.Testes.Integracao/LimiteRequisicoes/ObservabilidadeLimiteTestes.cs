using System.Net;
using FluentAssertions;
using PedidoFulfillment.Application.LimiteRequisicoes;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.LimiteRequisicoes;

/// <summary>As métricas valem para o processo todo: estes testes rodam sem nenhum outro em paralelo.</summary>
[CollectionDefinition(nameof(ColecaoMetricasLimite), DisableParallelization = true)]
public sealed class ColecaoMetricasLimite;

/// <summary>Logs, métricas e spans do limite, sem dados pessoais (REQ-23, FR-020, FR-021, SC-007, R-06).</summary>
[Collection(nameof(ColecaoMetricasLimite))]
public sealed class ObservabilidadeLimiteTestes
{
    private const string Email = "maria.souza@exemplo.com.br";
    private const string Metrica = "oms.limite_requisicoes.recusadas";
    private const string Atributo = "oms.limite.politica";

    [Fact]
    public async Task Um_log_por_contagem_por_janela_e_a_metrica_em_toda_recusa()
    {
        var logs = new CapturaLogs();
        using var telemetria = new ColetorTelemetria();
        await using var api = CenarioLimite.SemBanco(CenarioLimite.Limites(loginOrigemEmail: 1), logs: logs, telemetria: telemetria);
        var cliente = api.CreateClient();
        await CenarioLimite.LoginAsync(cliente, Email);

        for (var i = 0; i < 4; i++)
        {
            (await CenarioLimite.LoginAsync(cliente, Email)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }

        var evento = logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3007).Subject;
        evento.Texto.Should().Contain(PoliticaLimite.LoginOrigemEmail).And.Contain("m***@exemplo.com.br");
        telemetria.Soma(Metrica, (Atributo, PoliticaLimite.LoginOrigemEmail)).Should().Be(4);
    }

    [Fact]
    public async Task A_politica_por_origem_nao_cita_email_no_log()
    {
        var logs = new CapturaLogs();
        using var telemetria = new ColetorTelemetria();
        await using var api = CenarioLimite.SemBanco(CenarioLimite.Limites(registro: 1), logs: logs, telemetria: telemetria);
        var cliente = api.CreateClient();
        await cliente.PostAsync(CenarioLimite.RotaRegistro, null, TestContext.Current.CancellationToken);

        (await cliente.PostAsync(CenarioLimite.RotaRegistro, null, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3007).Which.Texto.Should().Contain(PoliticaLimite.Registro).And.NotContain("@");
        telemetria.Soma(Metrica, (Atributo, PoliticaLimite.Registro)).Should().Be(1);
    }

    [Fact]
    public async Task Logs_metricas_e_spans_nao_trazem_dados_pessoais()
    {
        var logs = new CapturaLogs();
        using var telemetria = new ColetorTelemetria();
        await using var api = CenarioLimite.SemBanco(CenarioLimite.Limites(loginOrigemEmail: 1), logs: logs, telemetria: telemetria);
        var cliente = api.CreateClient();
        await CenarioLimite.LoginAsync(cliente, Email, "SenhaSecreta123");
        await CenarioLimite.LoginAsync(cliente, Email, "SenhaSecreta123");

        // Do filtro de telemetria: o span da requisição termina logo depois da resposta.
        await telemetria.EsperarSpanAsync(s => s.DisplayName.Contains("auth/login", StringComparison.Ordinal), TimeSpan.FromSeconds(5));

        string[] proibidos = [Email, "SenhaSecreta123", CenarioLimite.Origem.ToString()];
        var atributosSpans = telemetria.Spans.SelectMany(s => s.TagObjects).Select(t => $"{t.Key}={t.Value}").ToArray();
        var atributosMetricas = telemetria.Medicoes.SelectMany(m => m.Atributos).Select(a => $"{a.Key}={a.Value}").ToArray();
        foreach (var proibido in proibidos)
        {
            logs.Textos.Should().NotContain(t => t.Contains(proibido, StringComparison.OrdinalIgnoreCase));
            atributosSpans.Should().NotContain(t => t.Contains(proibido, StringComparison.OrdinalIgnoreCase));
            atributosMetricas.Should().NotContain(t => t.Contains(proibido, StringComparison.OrdinalIgnoreCase));
        }
    }
}
