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

    /// <summary>Métrica 9 (REQ-18, R-19): entregas de webhook por resultado (entregue, falha, esgotada).</summary>
    public static readonly Counter<long> EntregasNotificacao = Medidor.CreateCounter<long>(
        "oms.notificacoes.entregas", "{entrega}", "Tentativas de entrega de webhook, por resultado.");

    /// <summary>Métrica 10 (REQ-18, R-19): do aviso gravado ao 2xx do receptor.</summary>
    public static readonly Histogram<double> LatenciaNotificacao = Medidor.CreateHistogram<double>(
        "oms.notificacoes.latencia", "s", "Tempo entre o aviso gravado e a resposta 2xx do receptor.");

    /// <summary>Métrica 11 (REQ-18, R-19), do <see cref="EstadoMetricas"/>.</summary>
    public static readonly ObservableGauge<long> EntregasPendentes = Medidor.CreateObservableGauge(
        "oms.notificacoes.pendentes",
        () => EstadoMetricas.Notificacoes is { } leitura ? [new Measurement<long>(leitura.EntregasPendentes)] : Array.Empty<Measurement<long>>(),
        "{entrega}", "Entregas de webhook pendentes.");

    /// <summary>Métrica 12 (REQ-18, R-19), do <see cref="EstadoMetricas"/>.</summary>
    public static readonly ObservableGauge<long> DestinosDesativados = Medidor.CreateObservableGauge(
        "oms.notificacoes.destinos_desativados",
        () => EstadoMetricas.Notificacoes is { } leitura ? [new Measurement<long>(leitura.DestinosDesativados)] : Array.Empty<Measurement<long>>(),
        "{destino}", "Destinos de webhook desativados.");

    /// <summary>Métrica 13 (REQ-18, R-19): conexões SSE abertas nesta instância da API.</summary>
    public static readonly ObservableGauge<long> ConexoesSse = Medidor.CreateObservableGauge(
        "oms.notificacoes.sse.conexoes", () => EstadoMetricas.ConexoesSse, "{conexao}", "Conexões de eventos (SSE) abertas.");

    /// <summary>Métrica 14 (REQ-19, R-12): chamadas ao gateway de pagamento, por operação e resultado final.</summary>
    public static readonly Counter<long> ChamadasPagamento = Medidor.CreateCounter<long>(
        "oms.pagamentos.chamadas", "{chamada}", "Chamadas ao gateway de pagamento, por operação e resultado.");

    /// <summary>Métrica 15 (REQ-19, R-12): duração de cada chamada, com todas as tentativas e esperas.</summary>
    public static readonly Histogram<double> DuracaoPagamento = Medidor.CreateHistogram<double>(
        "oms.pagamentos.duracao", "s", "Duração das chamadas ao gateway de pagamento, com as tentativas.");

    /// <summary>Métrica 16 (REQ-19, R-12), do <see cref="EstadoMetricas"/>: 0 fechado, 1 aberto, 2 meio-aberto.</summary>
    public static readonly ObservableGauge<long> CircuitoPagamento = Medidor.CreateObservableGauge(
        "oms.pagamentos.circuito",
        () => EstadoMetricas.CircuitoGateway is >= 0 and var estado ? [new Measurement<long>(estado)] : Array.Empty<Measurement<long>>(),
        "{estado}", "Estado do circuito do gateway de pagamento: 0 fechado, 1 aberto, 2 meio-aberto.");

    /// <summary>Métrica 17 (REQ-23, FR-021): requisições recusadas pelo limite, por política.</summary>
    public static readonly Counter<long> RequisicoesRecusadas = Medidor.CreateCounter<long>(
        "oms.limite_requisicoes.recusadas", "{requisicao}", "Requisições recusadas pelo limite, por política.");

    /// <summary>Métrica 18 (REQ-23, FR-021): requisições que seguiram sem o limite, com o armazenamento indisponível.</summary>
    public static readonly Counter<long> FalhasArmazenamentoLimite = Medidor.CreateCounter<long>(
        "oms.limite_requisicoes.falhas_armazenamento", "{requisicao}", "Requisições que seguiram sem o limite por falha do armazenamento.");

    /// <summary>Métrica 19 (REQ-24, R-10): renovações de sessão por resultado (renovada, recusada, reutilizada).</summary>
    public static readonly Counter<long> Renovacoes = Medidor.CreateCounter<long>(
        "oms.autenticacao.renovacoes", "{renovacao}", "Apresentações de token de renovação, por resultado.");

    /// <summary>Métrica 20 (REQ-24, R-10): e-mails por tipo e resultado (enviado, falha, descartado, sem_conta).</summary>
    public static readonly Counter<long> Emails = Medidor.CreateCounter<long>(
        "oms.emails", "{email}", "Solicitações de e-mail tratadas, por tipo e resultado.");

    /// <summary>
    /// Métrica 21 (REQ-28, FR-022a), do <see cref="EstadoMetricas"/>: estornos pendentes, separados pelo
    /// alerta (<c>oms.alerta</c> <c>true</c> além do limite, <c>false</c> os demais). Atualizada no começo de
    /// cada rodada de estornos que pega o lock.
    /// </summary>
    public static readonly ObservableGauge<long> EstornosPendentes = Medidor.CreateObservableGauge(
        "oms.estornos.pendentes",
        () => EstadoMetricas.Estornos is { } leitura
            ?
            [
                new Measurement<long>(leitura.AlemDoLimite, new KeyValuePair<string, object?>(Atributos.Alerta, "true")),
                new Measurement<long>(leitura.Pendentes - leitura.AlemDoLimite, new KeyValuePair<string, object?>(Atributos.Alerta, "false"))
            ]
            : Array.Empty<Measurement<long>>(),
        "{estorno}", "Estornos pendentes, separados pelo limite de alerta.");

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
        public const string PagamentoOperacao = "oms.pagamento.operacao";
        public const string PagamentoTentativa = "oms.pagamento.tentativa";
        public const string PagamentoTentativas = "oms.pagamento.tentativas";
        public const string PagamentoTipoFalha = "oms.pagamento.tipo_falha";
        public const string LimitePolitica = "oms.limite.politica";
        public const string EmailTipo = "oms.email.tipo";
        public const string Alerta = "oms.alerta";
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
        public const string Entregue = "entregue";
        public const string Falha = "falha";
        public const string Esgotada = "esgotada";

        // Gateway de pagamento (REQ-19, R-12).
        public const string Aprovado = "aprovado";
        public const string Recusado = "recusado";
        public const string EstornoRecusado = "estorno_recusado";
        public const string NaoEncontrada = "nao_encontrada";
        public const string FalhaPassageira = "falha_passageira";
        public const string Incerto = "incerto";
        public const string ErroPermanente = "erro_permanente";
        public const string CircuitoAberto = "circuito_aberto";

        /// <summary>Mensagem devolvida à fila porque o consumo pausou (REQ-19, R-09).</summary>
        public const string Pausado = "pausado";

        // Renovação de sessão (REQ-24, R-10).
        public const string Renovada = "renovada";
        public const string Recusada = "recusada";
        public const string Reutilizada = "reutilizada";

        // Envio de e-mail (REQ-24, R-10).
        public const string Enviado = "enviado";
        public const string Descartado = "descartado";
        public const string SemConta = "sem_conta";
    }

    /// <summary>Valores de <c>oms.rotina</c>.</summary>
    public static class Rotinas
    {
        public const string Reconciliacao = "reconciliacao";
        public const string Estornos = "estornos";
        public const string ReenvioDlq = "reenvio_dlq";
        public const string Limpeza = "limpeza";
        public const string Notificacoes = "notificacoes";
        public const string Emails = "emails";
    }

    /// <summary>Nomes dos spans próprios.</summary>
    public static class Spans
    {
        public const string Reconciliar = "reconciliar";
        public const string ReconciliarPedido = "reconciliar pedido";
        public const string ProcessarEstornos = "processar_estornos";
        public const string EstornarPedido = "estornar pedido";
        public const string Limpar = "limpar";
        public const string ProcessarNotificacoes = "processar_notificacoes";
        public const string ProcessarEmails = "processar_emails";
        public const string EnviarNotificacao = "notificacao enviar";
        public const string CobrarPagamento = "cobrar pagamento";
        public const string EstornarPagamento = "estornar pagamento";
        public const string ConsultarCobranca = "consultar cobranca";

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
