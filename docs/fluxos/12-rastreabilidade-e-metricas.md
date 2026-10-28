# Fluxo 12: rastreabilidade e métricas

Transversal: roda na API e no Worker. Spec: [REQ-16](../../specs/016-observabilidade-opentelemetry/spec.md);
as métricas de inscrição dos consumidores são do [REQ-17](../../specs/017-recuperacao-consumidores/contracts/recuperacao-consumo.md).
Nomes estáveis (spans, atributos, métricas): [contrato de telemetria](../../specs/016-observabilidade-opentelemetry/contracts/telemetria.md).
Decisões: [research](../../specs/016-observabilidade-opentelemetry/research.md).

## Para que serve

Seguir um pedido de ponta a ponta numa única linha do tempo, da requisição até o consumidor de
entrega, atravessando o outbox e o RabbitMQ, e acompanhar a mensageria por métricas, sem
remontar a sequência à mão pelos logs dos dois processos. A exportação usa o protocolo aberto
OTLP, e o destino se troca por configuração.

## Ligar e desligar

| Situação | Comportamento |
|----------|---------------|
| Sem `OTEL_EXPORTER_OTLP_ENDPOINT` | Desligada: nenhum span, nenhuma métrica, nenhuma coluna `ContextoRastreio` gravada, nenhum `traceparent` nas mensagens. O comportamento é o de antes do REQ-16 |
| Com `OTEL_EXPORTER_OTLP_ENDPOINT` | Ligada: traces, métricas e logs exportados em lote para o destino |
| `Observabilidade:Habilitada = true` | Ligada sem exportar (usado pelos testes, que coletam em memória) |

Por quê: FR-020. A coluna só é gravada com o SDK ligado, porque o ASP.NET Core cria uma
activity em toda requisição, mesmo sem OpenTelemetry.

## Caminho feliz: o trace de um pedido

```text
POST /api/v1/pedidos/                               (API, user.id, oms.pedido.id)
└─ INSERT Pedidos, EventosSaida                     (contexto gravado em ContextoRastreio)
   └─ outbox publicar pedido.criado                 (Worker, oms.outbox.espera_ms)
      └─ publish → deliver oms.estoque              (oms.consumidor=estoque, oms.resultado=processado)
         └─ outbox publicar estoque.reservado
            └─ publish → deliver oms.pagamento
               └─ outbox publicar pagamento.aprovado
                  └─ publish → deliver oms.entrega
```

1. A API grava o pedido e o `pedido.criado` no outbox. Um interceptor do EF Core grava na
   coluna `ContextoRastreio` das linhas novas o contexto W3C da requisição
   (`00-<trace>-<span>-<flags>`).
2. O despachante lê o evento e abre `outbox publicar {tipo}` com esse contexto como pai.
   `oms.outbox.espera_ms` mostra quanto o evento esperou no outbox.
3. O RabbitMQ.Client 7 cria o span de publicação e grava o cabeçalho de transporte
   `traceparent`, ao lado de `x-tentativas` e `x-reenvios`. O envelope v1 não muda.
4. Na entrega, o cliente cria o span `deliver` como filho da publicação. O consumidor o marca
   com `oms.consumidor`, `oms.evento.tipo`, `oms.pedido.id`, `oms.tentativa` e
   `oms.resultado`.
5. O evento que o consumidor grava herda o `deliver` como contexto, e o ciclo se repete até a
   entrega.

As consultas ao banco feitas dentro de cada passo aparecem como filhas, com o texto
parametrizado (`... WHERE [p].[Id] = @id`) e nunca com os valores (clarify 4).

## Novas tentativas, DLQ e duplicado

| Caso | No trace | Por quê |
|------|----------|---------|
| Falha com nova tentativa | `deliver` com erro, `error.type` e `oms.resultado = nova_tentativa`; a tentativa seguinte é outro `deliver` no mesmo trace | A republicação na fila de espera acontece dentro do `deliver`, e o cliente grava o `traceparent` atual |
| Esgotou as tentativas | Último `deliver` com erro e `oms.resultado = dlq` | Idem |
| Mensagem ilegível | `oms.resultado = irrecuperavel`, `error.type = MensagemIrrecuperavel` | Vai direto para a DLQ (REQ-06) |
| Mensagem repetida | `oms.resultado = duplicado`, sem erro | O consumidor descarta pelo registro de processado |
| Contexto inválido no outbox ou no cabeçalho | Ignorado; a publicação ou o consumo começam um trace novo, sem erro | FR-013 |

## Reenvio da DLQ

- Cada pedido de reenvio executado é um trace próprio, `reenviar_dlq {consumidor}`, com
  `oms.reenvio.pedido_id`, as contagens e um link para a requisição do Admin que o registrou.
- Cada mensagem reenviada ganha `reenviar {tipo}`, filho do contexto que a mensagem trazia na
  DLQ e com link para `reenviar_dlq`. O consumo da cópia continua no trace original do pedido.
- Mensagem sem contexto (publicada com a telemetria desligada): `reenviar {tipo}` fica no
  trace da execução.

Por quê: o cliente sobrescreve o `traceparent` ao publicar; o reenvio lê o original antes
(REQ-16, research R-04).

## Rotinas periódicas

| Rotina | Span raiz | Passos | Link |
|--------|-----------|--------|------|
| Reconciliação | `reconciliar` | `reconciliar pedido`, com `oms.etapa` e `oms.resultado` | Trace de criação do pedido |
| Estornos | `processar_estornos` | `estornar pedido`, com `oms.resultado` | Trace de criação do pedido |
| Limpeza | `limpar` | `limpar {alvo}`, com `oms.limpeza.linhas_apagadas` | — |

- A rodada que não pega o lock (logs 9006, 7011 e 5015) não gera span.
- O evento gravado por uma rotina (por exemplo, `estoque.reservado` gravado pela
  reconciliação) continua o passo dela.
- Pedido criado com a telemetria desligada ou antes do REQ-16: o passo sai sem link.

Por quê: um trace por rodada, com links, segue a convenção para trabalho em lote; continuar o
trace de criação deixaria traces com dias de duração (research R-05).

## Métricas

Meter `PedidoFulfillment`, emitidas pelo Worker:

| Métrica | Tipo | Unidade | Atributos | Atualização |
|---------|------|---------|-----------|-------------|
| `oms.outbox.eventos_pendentes` | gauge | `{evento}` | — | Leitura do banco a cada 30 s |
| `oms.outbox.pendente_mais_antigo.idade` | gauge | `s` | — | Idem; 0 sem pendentes |
| `oms.mensageria.dlq.mensagens` | gauge | `{mensagem}` | `oms.consumidor` | Leitura do broker a cada 60 s |
| `oms.mensageria.mensagens` | contador | `{mensagem}` | `oms.consumidor`, `oms.resultado` | A cada mensagem |
| `oms.rotinas.itens` | contador | `{item}` | `oms.rotina`, `oms.resultado` | A cada pedido tratado (reconciliação, estornos) e a cada pedido de reenvio |
| `oms.limpeza.linhas_apagadas` | contador | `{linha}` | `oms.limpeza.alvo` | A cada rodada de limpeza |
| `oms.mensageria.consumidor.inscrito` | gauge | `{inscricao}` | `oms.consumidor` | Estado em memória: 1 inscrito, 0 sem inscrição (REQ-17) |
| `oms.mensageria.consumidor.reinscricoes` | contador | `{reinscricao}` | `oms.consumidor` | A cada volta depois de uma perda de inscrição; a da partida não conta (REQ-17) |

Também saem as métricas nativas do .NET (`System.Runtime`) e do ASP.NET Core.

- **Atraso de ponta a ponta**: leitura mais envio. O envio é a cada 30 s
  (`OTEL_METRIC_EXPORT_INTERVAL` muda). Com os padrões, um outbox parado aparece no destino em
  até 60 s, e a DLQ em até 90 s.
- **Várias instâncias do Worker**: cada uma publica as suas, com `service.instance.id`. Os
  gauges saem iguais de todas: agregue com `max`. Os contadores se somam.
- **Aviso 5011**: continua, no máximo uma vez a cada `ReenvioDlq:AvisoIntervaloSegundos` (5 min)
  por consumidor, mesmo com a DLQ lida a cada minuto para a métrica.

## Logs

- Todo log emitido dentro de um passo leva `TraceId` e `SpanId`. No destino OTLP, são campos
  do registro; no console (`docker compose logs`), aparecem nos escopos.
- Nenhum ID de evento, nome, nível ou mensagem mudou.

Exemplo no console:

```text
info: ConsumidorEventos[5006]
      => SpanId:b06267cf351d37f5, TraceId:67c6b66f77a3da034e1aa1953cf2c576
      Evento 01a17ac7-b989-769c-bb7a-2c5dd9d744a9 processado por entrega.
```

## Configuração

Seção `Observabilidade` (API e Worker, validada na partida):

| Chave | Padrão | Faixa | Uso |
|-------|--------|-------|-----|
| `Habilitada` | `false` | — | Liga sem destino (testes) |
| `TaxaAmostragem` | 1,0 | 0 a 1 | Razão da amostragem na origem do trace |
| `IntervaloAmostragemOutboxSegundos` | 30 | 5 a 300 | Leitura do outbox para as métricas (Worker) |

Seção `ReenvioDlq`: `MetricaIntervaloSegundos` (padrão 60, de 10 a 300), a leitura da DLQ para a
métrica, só com a telemetria ligada.

Variáveis padrão do OpenTelemetry: `OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL`
(padrão `grpc`), `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_SERVICE_NAME` (padrão `oms-api` e
`oms-worker`), `OTEL_RESOURCE_ATTRIBUTES` e `OTEL_METRIC_EXPORT_INTERVAL`.

Fora das faixas, o processo não sobe e a mensagem cita a chave, por exemplo
"Observabilidade:TaxaAmostragem deve estar entre 0 e 1.".

## Amostragem

- O pedido é amostrado ou não por inteiro: a decisão da requisição viaja no `traceparent`,
  inclusive pelo outbox e pelo broker.
- Consultas ao banco sem passo pai (o despachante a cada 1 s, os locks das rotinas) nunca
  viram trace.
- Métricas e logs não são amostrados.

## Destinos

| Destino | Como apontar |
|---------|--------------|
| Painel local (Aspire Dashboard) | Perfil `demo` (já aponta) ou perfil `observabilidade` com `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317`; interface em `http://localhost:18888` |
| Jaeger | OTLP direto em `:4317` (só traces) |
| Grafana Tempo, Prometheus/Mimir, Loki | OpenTelemetry Collector ou Grafana Alloy recebendo OTLP |
| Azure Monitor | OpenTelemetry Collector com o exportador `azuremonitor` |

Nenhum destino exige mudar código.

## Falhas externas

| Falha | Comportamento |
|-------|---------------|
| Destino fora do ar ou lento | A API e o Worker seguem normalmente: a exportação é em lote, em segundo plano, e descarta o que não consegue enviar (FR-021) |
| Banco fora do ar | O amostrador do outbox mantém a última leitura (log Debug, sem ID novo); o despachante já avisa com o 5007 |
| Broker fora do ar | O gauge da DLQ mantém a última leitura; o 5012 continua |
| Painel local reiniciado | Os dados guardados nele se perdem (fica em memória) |

## Dados que nunca saem

E-mail, senha, token, cabeçalho `Authorization`, corpo de requisição ou de mensagem, query
string e valores de parâmetros de SQL. O usuário aparece só como `user.id` (o identificador do
token). Um teste varre a telemetria exportada em busca desses dados (SC-006).

## Limitações

- Não há alertas: as métricas existem, mas nenhuma regra dispara aviso fora dos logs.
- A latência por etapa do pedido não é métrica; aparece só no trace de cada pedido.
- Um chamador que manda `traceparent` próprio continua o trace dele e decide a amostragem dos
  próprios pedidos (o `ParentBased` respeita a flag). Não expõe dados.
- O painel local não tem autenticação e guarda tudo em memória: serve só para
  desenvolvimento e demonstração.
- Não há instrumentação de HTTP de saída: os gateways de pagamento e de estorno são simulados.
