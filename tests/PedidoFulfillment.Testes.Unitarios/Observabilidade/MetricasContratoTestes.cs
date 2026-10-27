using System.Diagnostics.Metrics;
using FluentAssertions;
using PedidoFulfillment.Infrastructure.Observabilidade;

namespace PedidoFulfillment.Testes.Unitarios.Observabilidade;

/// <summary>Nomes, unidades e tipos das seis métricas do contrato (REQ-16, FR-016; contracts/telemetria.md).</summary>
public sealed class MetricasContratoTestes
{
    public static TheoryData<Instrument, string, string, Type> Metricas => new()
    {
        { TelemetriaOms.EventosPendentes, "oms.outbox.eventos_pendentes", "{evento}", typeof(ObservableGauge<long>) },
        { TelemetriaOms.IdadePendenteMaisAntigo, "oms.outbox.pendente_mais_antigo.idade", "s", typeof(ObservableGauge<double>) },
        { TelemetriaOms.MensagensDlq, "oms.mensageria.dlq.mensagens", "{mensagem}", typeof(ObservableGauge<long>) },
        { TelemetriaOms.Mensagens, "oms.mensageria.mensagens", "{mensagem}", typeof(Counter<long>) },
        { TelemetriaOms.ItensRotinas, "oms.rotinas.itens", "{item}", typeof(Counter<long>) },
        { TelemetriaOms.LinhasApagadas, "oms.limpeza.linhas_apagadas", "{linha}", typeof(Counter<long>) }
    };

    [Theory]
    [MemberData(nameof(Metricas))]
    public void Metrica_segue_o_contrato(Instrument instrumento, string nome, string unidade, Type tipo)
    {
        (instrumento.Name, instrumento.Unit, instrumento.GetType(), instrumento.Meter.Name)
            .Should().Be((nome, unidade, tipo, TelemetriaOms.Nome));
    }

    [Fact]
    public void Gauges_leem_o_estado_sem_ir_ao_banco()
    {
        // Cria os instrumentos estáticos antes de ligar o ouvinte, que publica os já existentes.
        _ = TelemetriaOms.EventosPendentes;
        using var ouvinte = new MeterListener();
        var lidas = new List<(string Nome, double Valor)>();
        ouvinte.InstrumentPublished = (instrumento, l) =>
        {
            if (instrumento.Meter.Name == TelemetriaOms.Nome)
            {
                l.EnableMeasurementEvents(instrumento);
            }
        };
        ouvinte.SetMeasurementEventCallback<long>((i, v, _, _) => lidas.Add((i.Name, v)));
        ouvinte.SetMeasurementEventCallback<double>((i, v, _, _) => lidas.Add((i.Name, v)));
        ouvinte.Start();

        EstadoMetricas.RegistrarOutbox(7, 42.5);
        EstadoMetricas.RegistrarDlq("estoque", 3);
        ouvinte.RecordObservableInstruments();

        lidas.Should().Contain(("oms.outbox.eventos_pendentes", 7)).And.Contain(("oms.outbox.pendente_mais_antigo.idade", 42.5))
            .And.Contain(("oms.mensageria.dlq.mensagens", 3));
    }
}
