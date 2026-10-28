using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PedidoFulfillment.Infrastructure.Observabilidade;

/// <summary>
/// Fonte de spans, medidor e nomes da telemetria própria do OMS (REQ-16,
/// <c>contracts/telemetria.md</c>). Só BCL: sem ouvinte (telemetria desligada), criar spans e
/// medir não custa nada. Os nomes são contrato: mudá-los exige registro na spec.
/// </summary>
internal static class TelemetriaOms
{
    public const string Nome = "PedidoFulfillment";

    public static readonly ActivitySource Fonte = new(Nome);

    public static readonly Meter Medidor = new(Nome);

    // Métricas do contrato (FR-016), com nome e unidade UCUM. Sem ouvinte, medir não custa nada.

    /// <summary>Métrica 1, do <see cref="EstadoMetricas"/>.</summary>
    public static readonly ObservableGauge<long> EventosPendentes = Medidor.CreateObservableGauge(
        "oms.outbox.eventos_pendentes",
        () => EstadoMetricas.Outbox is { } leitura ? [new Measurement<long>(leitura.Pendentes)] : Array.Empty<Measurement<long>>(),
        "{evento}", "Eventos pendentes no outbox.");

    /// <summary>Métrica 2, do <see cref="EstadoMetricas"/>; zero sem pendentes.</summary>
    public static readonly ObservableGauge<double> IdadePendenteMaisAntigo = Medidor.CreateObservableGauge(
        "oms.outbox.pendente_mais_antigo.idade",
        () => EstadoMetricas.Outbox is { } leitura ? [new Measurement<double>(leitura.IdadeSegundos)] : Array.Empty<Measurement<double>>(),
        "s", "Idade do evento pendente mais antigo do outbox.");

    /// <summary>Métrica 3, por consumidor, do <see cref="EstadoMetricas"/>.</summary>
    public static readonly ObservableGauge<long> MensagensDlq = Medidor.CreateObservableGauge(
        "oms.mensageria.dlq.mensagens",
        () => EstadoMetricas.MensagensDlq.Select(d => new Measurement<long>(d.Value, new KeyValuePair<string, object?>(Atributos.Consumidor, d.Key))),
        "{mensagem}", "Mensagens na DLQ de cada consumidor.");

    /// <summary>Métrica 4: mensagens tratadas pelos consumidores, por consumidor e resultado.</summary>
    public static readonly Counter<long> Mensagens = Medidor.CreateCounter<long>(
        "oms.mensageria.mensagens", "{mensagem}", "Mensagens tratadas pelos consumidores.");

    /// <summary>Métrica 5: itens tratados pelas rotinas, por rotina e resultado.</summary>
    public static readonly Counter<long> ItensRotinas = Medidor.CreateCounter<long>(
        "oms.rotinas.itens", "{item}", "Itens tratados pela reconciliação, pelos estornos e pelo reenvio da DLQ.");

    /// <summary>Métrica 6: linhas apagadas pela limpeza, por alvo.</summary>
    public static readonly Counter<long> LinhasApagadas = Medidor.CreateCounter<long>(
        "oms.limpeza.linhas_apagadas", "{linha}", "Linhas apagadas pela limpeza.");

    /// <summary>Métrica 7 (REQ-17, FR-016a): 1 com o consumidor inscrito, 0 sem inscrição, do <see cref="EstadoMetricas"/>.</summary>
    public static readonly ObservableGauge<long> ConsumidorInscrito = Medidor.CreateObservableGauge(
        "oms.mensageria.consumidor.inscrito",
        () => EstadoMetricas.ConsumidoresInscritos.Select(i => new Measurement<long>(i.Value, new KeyValuePair<string, object?>(Atributos.Consumidor, i.Key))),
        "{inscricao}", "Se cada consumidor está inscrito na fila principal (1) ou não (0).");

    /// <summary>Métrica 8 (REQ-17, FR-016a): inscrições refeitas depois de uma perda, por consumidor.</summary>
    public static readonly Counter<long> Reinscricoes = Medidor.CreateCounter<long>(
        "oms.mensageria.consumidor.reinscricoes", "{reinscricao}", "Vezes que um consumidor voltou a se inscrever depois de perder a inscrição.");

    /// <summary>Atributos dos spans e das métricas (prefixo <c>oms.</c> para os de domínio).</summary>
    public static class Atributos
    {
        public const string PedidoId = "oms.pedido.id";
        public const string EventoTipo = "oms.evento.tipo";
        public const string Consumidor = "oms.consumidor";
        public const string Tentativa = "oms.tentativa";
        public const string Resultado = "oms.resultado";
        public const string Rotina = "oms.rotina";
        public const string Etapa = "oms.etapa";
        public const string ReenvioPedidoId = "oms.reenvio.pedido_id";
        public const string ReenvioReenviadas = "oms.reenvio.reenviadas";
        public const string ReenvioBloqueadas = "oms.reenvio.bloqueadas";
        public const string ReenvioForaDoFiltro = "oms.reenvio.fora_do_filtro";
        public const string LimpezaAlvo = "oms.limpeza.alvo";
        public const string LimpezaLinhasApagadas = "oms.limpeza.linhas_apagadas";
        public const string OutboxEsperaMs = "oms.outbox.espera_ms";
        public const string UsuarioId = "user.id";
        public const string MensagemId = "messaging.message.id";
        public const string TipoErro = "error.type";
    }

    /// <summary>Valores de <c>oms.resultado</c>.</summary>
    public static class Resultados
    {
        public const string Processado = "processado";
        public const string Duplicado = "duplicado";
        public const string NovaTentativa = "nova_tentativa";
        public const string Dlq = "dlq";
        public const string Irrecuperavel = "irrecuperavel";
        public const string Recuperado = "recuperado";
        public const string Esgotado = "esgotado";
        public const string Falhou = "falhou";
        public const string Confirmado = "confirmado";
        public const string Concluido = "concluido";
    }

    /// <summary>Valores de <c>oms.rotina</c>.</summary>
    public static class Rotinas
    {
        public const string Reconciliacao = "reconciliacao";
        public const string Estornos = "estornos";
        public const string ReenvioDlq = "reenvio_dlq";
        public const string Limpeza = "limpeza";
    }

    /// <summary>Nomes dos spans próprios.</summary>
    public static class Spans
    {
        public const string Reconciliar = "reconciliar";
        public const string ReconciliarPedido = "reconciliar pedido";
        public const string ProcessarEstornos = "processar_estornos";
        public const string EstornarPedido = "estornar pedido";
        public const string Limpar = "limpar";

        public static string OutboxPublicar(string tipo) => $"outbox publicar {tipo}";

        public static string ReenviarDlq(string consumidor) => $"reenviar_dlq {consumidor}";

        public static string Reenviar(string? tipo) => $"reenviar {tipo}";

        public static string LimparAlvo(string alvo) => $"limpar {alvo}";
    }

    /// <summary>Um link para o contexto gravado, se ele for válido; senão, nenhum (FR-013).</summary>
    public static ActivityLink[] LigacaoPara(string? contexto) =>
        ContextoRastreio.TentarLer(contexto, out var lido) ? [new ActivityLink(lido)] : [];

    /// <summary>Soma um item tratado por uma rotina (métrica 5).</summary>
    public static void ContarItem(string rotina, string resultado) =>
        ItensRotinas.Add(1, new(Atributos.Rotina, rotina), new(Atributos.Resultado, resultado));

    /// <summary>Marca o span com erro e o tipo do erro (convenção <c>error.type</c>).</summary>
    public static void MarcarErro(this Activity? activity, string tipoErro)
    {
        activity?.SetStatus(ActivityStatusCode.Error);
        activity?.SetTag(Atributos.TipoErro, tipoErro);
    }
}
