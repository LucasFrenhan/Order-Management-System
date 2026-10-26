# Contrato de telemetria do OMS (REQ-16)

**Spec**: [spec.md](../spec.md) | **Research**: [research.md](../research.md) | **Modelo**: [data-model.md](../data-model.md)

Contrato para quem consome a telemetria (painéis, consultas, alertas futuros). Os nomes abaixo
são estáveis: mudar um deles é mudança de contrato e exige registro na spec do REQ que mudar.
Os eventos de negócio (envelope v1) e a API pública **não** fazem parte deste contrato e não
mudam.

## Recurso

| Atributo | API | Worker |
|----------|-----|--------|
| `service.name` | `oms-api` | `oms-worker` |
| `service.instance.id` | GUID por processo (SDK) | GUID por processo (SDK) |
| `deployment.environment.name` | por `OTEL_RESOURCE_ATTRIBUTES` | idem |

## Fontes

| Fonte (`ActivitySource`/`Meter`) | Origem |
|----------------------------------|--------|
| `PedidoFulfillment` | Spans e métricas próprios do OMS |
| `Microsoft.AspNetCore` (via instrumentação) | Requisições da API, exceto `/health` e `/health/pronto` |
| `OpenTelemetry.Instrumentation.SqlClient` | Consultas ao banco com span pai |
| `RabbitMQ.Client.Publisher`, `RabbitMQ.Client.Subscriber` | Publicação, entrega (`deliver`) e leitura (`fetch`) |
| meters `System.Runtime`, `Microsoft.AspNetCore.Hosting`, `Microsoft.AspNetCore.Server.Kestrel` | Métricas nativas do .NET |

A fonte `RabbitMQ.Client.Connection` não é registrada (tentativas de conexão viram ruído).

## Spans próprios

| Nome | Tipo | Pai | Links | Atributos |
|------|------|-----|-------|-----------|
| `outbox publicar {tipo}` | Internal | contexto gravado no evento (ou raiz, se nulo/inválido) | — | `oms.evento.tipo`, `messaging.message.id`, `oms.outbox.espera_ms` |
| `reenviar_dlq {consumidor}` | Internal | raiz | requisição que registrou o pedido de reenvio | `oms.reenvio.pedido_id`, `oms.consumidor`, `oms.reenvio.reenviadas`, `oms.reenvio.bloqueadas`, `oms.reenvio.fora_do_filtro`, `oms.resultado` |
| `reenviar {tipo}` | Internal | `traceparent` da mensagem na DLQ (ou o span anterior, se ausente) | `reenviar_dlq {consumidor}` | `oms.evento.tipo`, `messaging.message.id`, `oms.consumidor`, `oms.reenvio.pedido_id` |
| `reconciliar` | Internal | raiz | — | `oms.rotina = reconciliacao`, contagens por resultado |
| `reconciliar pedido` | Internal | `reconciliar` | criação do pedido | `oms.pedido.id`, `oms.etapa`, `oms.resultado` |
| `processar_estornos` | Internal | raiz | — | `oms.rotina = estornos`, contagens |
| `estornar pedido` | Internal | `processar_estornos` | criação do pedido | `oms.pedido.id`, `oms.resultado` |
| `limpar` | Internal | raiz | — | `oms.rotina = limpeza` |
| `limpar {alvo}` | Internal | `limpar` | — | `oms.limpeza.alvo`, `oms.limpeza.linhas_apagadas` |

Rodadas que não pegam o lock e ciclos do despachante sem eventos não geram span.

### Atributos acrescentados a spans de terceiros

| Span | Atributos |
|------|-----------|
| Requisição HTTP (ASP.NET Core) | `user.id` (o `sub` do token; nunca o e-mail), `oms.pedido.id` nas rotas de pedido |
| `deliver` (RabbitMQ.Client) | `oms.consumidor`, `oms.evento.tipo`, `oms.pedido.id`, `oms.tentativa`, `oms.resultado`; erro com `error.type` quando a mensagem vai para nova tentativa, DLQ ou é irrecuperável |

### Valores dos atributos enumerados

| Atributo | Valores |
|----------|---------|
| `oms.consumidor` | `estoque`, `pagamento`, `entrega` |
| `oms.resultado` (consumo) | `processado`, `duplicado`, `nova_tentativa`, `dlq`, `irrecuperavel` |
| `oms.resultado` (reconciliação) | `recuperado`, `esgotado`, `falhou` |
| `oms.resultado` (estornos) | `confirmado`, `falhou` |
| `oms.resultado` (reenvio da DLQ) | `concluido`, `falhou` |
| `oms.rotina` | `reconciliacao`, `estornos`, `reenvio_dlq`, `limpeza` |
| `oms.etapa` | `Estoque`, `Pagamento`, `Entrega` |
| `oms.limpeza.alvo` | `enviados`, `processados`, `chaves`, `pedidos_reenvio` |

## Métricas próprias (meter `PedidoFulfillment`)

| # | Nome | Instrumento | Unidade | Atributos | Emitida por |
|---|------|-------------|---------|-----------|-------------|
| 1 | `oms.outbox.eventos_pendentes` | ObservableGauge (long) | `{evento}` | — | Worker |
| 2 | `oms.outbox.pendente_mais_antigo.idade` | ObservableGauge (double) | `s` | — | Worker |
| 3 | `oms.mensageria.dlq.mensagens` | ObservableGauge (long) | `{mensagem}` | `oms.consumidor` | Worker |
| 4 | `oms.mensageria.mensagens` | Counter (long) | `{mensagem}` | `oms.consumidor`, `oms.resultado` | Worker |
| 5 | `oms.rotinas.itens` | Counter (long) | `{item}` | `oms.rotina`, `oms.resultado` | Worker |
| 6 | `oms.limpeza.linhas_apagadas` | Counter (long) | `{linha}` | `oms.limpeza.alvo` | Worker |

- **Várias instâncias**: os gauges 1 a 3 saem iguais de cada instância; agregue com `max`.
  Os contadores 4 a 6 se somam.
- **Atraso**: 1 e 2 até `IntervaloAmostragemOutboxSegundos` (30 s); 3 até
  `MetricaIntervaloSegundos` (60 s); mais o intervalo de exportação do SDK (60 s por padrão,
  `OTEL_METRIC_EXPORT_INTERVAL`).
- Os gauges só existem com a telemetria ligada; sem leitura ainda, não emitem ponto.

## Logs

- Todo log emitido dentro de um span leva `TraceId` e `SpanId`; no OTLP, como campos do
  registro; no console, no escopo (`ActivityTrackingOptions` e `IncludeScopes`).
- `EventId`, `EventName`, nível, mensagem e atributos dos logs existentes **não mudam**.
- Nenhum log novo com ID novo é criado por este requisito.

## Propagação

- Formato W3C Trace Context (`traceparent`); `tracestate` e baggage não são usados.
- HTTP: a API aceita `traceparent` de entrada (o chamador pode continuar o próprio trace).
- Broker: cabeçalho `traceparent` gravado pelo RabbitMQ.Client (ver
  [data-model.md](../data-model.md#mensagem-no-broker)).
- Outbox, pedido e pedido de reenvio: coluna `ContextoRastreio`.

## Amostragem

- `AmostradorSemSqlRaiz` em volta de `ParentBased(TraceIdRatioBased(Observabilidade:TaxaAmostragem))`:
  span raiz da fonte do SqlClient é sempre descartado; o resto segue a decisão do pai, ou a
  taxa quando é raiz.
- A flag de amostragem viaja no `traceparent`; o pedido inteiro é amostrado ou não.
- Métricas e logs não são amostrados.

## Proibido em qualquer sinal exportado

E-mail, senha, token, cabeçalho `Authorization`, corpo de requisição ou de mensagem, query
string (mascarada pela instrumentação), valores de parâmetros de SQL, nome, documento ou
endereço do Cliente. O texto parametrizado das consultas é permitido (clarify, pergunta 4).

## Configuração de destino

| Variável | Exemplo | Efeito |
|----------|---------|--------|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://aspire-dashboard:18889` | Liga a telemetria e define o destino |
| `OTEL_EXPORTER_OTLP_PROTOCOL` | `grpc` (padrão) ou `http/protobuf` | Protocolo |
| `OTEL_EXPORTER_OTLP_HEADERS` | `x-otlp-api-key=...` | Autenticação do destino |
| `OTEL_SERVICE_NAME` | `oms-api` | Sobrescreve o nome do serviço |
| `OTEL_RESOURCE_ATTRIBUTES` | `deployment.environment.name=homologacao` | Atributos do ambiente |

| Destino | Como apontar |
|---------|--------------|
| Aspire Dashboard (local) | `http://aspire-dashboard:18889` na rede do compose; `http://localhost:4317` pelo SDK |
| Jaeger | OTLP direto em `:4317` (só traces) |
| Grafana Tempo, Prometheus/Mimir, Loki | OpenTelemetry Collector ou Grafana Alloy recebendo OTLP |
| Azure Monitor | OpenTelemetry Collector com o exportador `azuremonitor` |
