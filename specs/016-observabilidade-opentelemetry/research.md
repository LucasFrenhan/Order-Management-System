# Research: rastreabilidade com OpenTelemetry (estudo da issue #37)

**Issue**: [#37](https://github.com/LucasFrenhan/Order-Management-System/issues/37) | **Data**: 2026-10-26 | **Situação**: aprovada (adotar parcialmente); revisada no plano

Este documento é o primeiro entregável da issue #37: a decisão fundamentada sobre adotar o
OpenTelemetry, com as alternativas e o custo de cada parte. Se a decisão for adotar, ele vira
a base do `/speckit-specify` do REQ-16, e o `plan.md` herda as decisões R-01 a R-12.

## Decisão proposta

**Adotar parcialmente**, em um REQ (REQ-16), com três partes e uma fora:

| Parte | Decisão | Custo |
|-------|---------|-------|
| Traces de ponta a ponta (ASP.NET Core, SqlClient, RabbitMQ.Client nativo, propagação pelo outbox, reenvio da DLQ) | Adotar | Médio |
| Logs correlacionados (`TraceId`/`SpanId`) e exportados por OTLP | Adotar | Pequeno |
| Métricas próprias: 6 instrumentos (R-07) | Adotar | Médio |
| Latência por etapa do pedido como métrica | Adiar (o trace já mostra por pedido) | — |

O ponto que decide a adoção é que o **RabbitMQ.Client 7.2.2, já em uso, cria os spans de
publicação e consumo e propaga o `traceparent` sozinho**, sem pacote extra e sem MassTransit
(R-02). O que falta é costurar os dois pontos que o cliente não enxerga: o outbox (R-03) e o
reenvio da DLQ (R-04). As duas costuras são pequenas e locais.

Pacotes NuGet novos: **4** (R-01), todos estáveis. Nenhum contrato muda: o envelope v1 e a API
pública ficam iguais; muda uma coluna nova no `EventoSaida` e outra no `Pedido`.

## R-01 Pacotes e o que já vem do .NET 10

**Decisão**: quatro pacotes, na série 1.19 (a 1.20.x saiu há menos de duas semanas, regra já
usada no `Directory.Packages.props`):

| Pacote | Versão | Onde | Por quê |
|--------|--------|------|---------|
| `OpenTelemetry.Extensions.Hosting` | 1.19.1 | Infrastructure | SDK e integração com o host (traces, métricas e logs) |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 1.19.1 | Infrastructure | Exportador OTLP, neutro em fornecedor (objetivo 4) |
| `OpenTelemetry.Instrumentation.AspNetCore` | 1.19.0 | API | Spans das requisições com `http.route` e os filtros de rota |
| `OpenTelemetry.Instrumentation.SqlClient` | 1.19.0 | Infrastructure | Spans das consultas; o EF Core usa o SqlClient por baixo |

**O que não precisa de pacote**:
- métricas do runtime: o .NET 9+ publica o meter `System.Runtime` (basta `AddMeter`);
- métricas do ASP.NET Core e do Kestrel: meters nativos `Microsoft.AspNetCore.*`;
- spans do RabbitMQ: `ActivitySource` nativas do RabbitMQ.Client 7 (R-02);
- spans e métricas próprios: `System.Diagnostics.ActivitySource` e `Meter`, da BCL. A
  Application continua sem pacote novo.

**Rejeitados**:
- `OpenTelemetry.Instrumentation.EntityFrameworkCore`: ainda beta (1.20.1-beta.1); o SqlClient
  cobre as consultas;
- `OpenTelemetry.Instrumentation.Http`: não há `HttpClient` no projeto. Os gateways de
  pagamento e de estorno são simulados (`GatewayPagamentoSimulado`). Entra quando houver um
  gateway real;
- `OpenTelemetry.Instrumentation.Runtime`: o meter nativo já cobre;
- `RabbitMQ.Client.OpenTelemetry`: só em `1.0.0-rc.2`, e só ajusta as opções estáticas que dá
  para ajustar direto (R-02);
- exportadores de fornecedor (Azure Monitor, Jaeger nativo): acoplam o código; OTLP + coletor
  resolve (R-10).

**Risco a validar no plano**: o Microsoft.Data.SqlClient 7.1.1 é recente. A instrumentação do
SqlClient escuta o `DiagnosticListener` do driver; o plano precisa confirmar com um teste que os
spans aparecem com a 7.x. Se não aparecerem, a alternativa é ficar sem spans de SQL (o resto do
trace continua de pé) e reabrir o ponto quando a instrumentação suportar.

## R-02 RabbitMQ.Client 7.2.2: o que já vem pronto

Conferido no código-fonte da versão 7.2 (`RabbitMQActivitySource`, `RabbitMQTracingOptions`,
`AsyncConsumerDispatcher`, `Channel.BasicPublish`):

- três `ActivitySource`: `RabbitMQ.Client.Publisher`, `RabbitMQ.Client.Subscriber` e
  `RabbitMQ.Client.Connection`. Sem ouvinte registrado, nenhuma activity é criada (custo zero);
- `BasicPublishAsync` cria um span `Producer` filho de `Activity.Current` e **injeta o
  `traceparent` nos cabeçalhos da mensagem** pelo `DistributedContextPropagator.Current` (W3C).
  Se o cabeçalho já existir, é sobrescrito;
- a entrega a um `AsyncEventingBasicConsumer` roda o callback **dentro** de um span `Consumer`
  (`deliver`). Com `UsePublisherAsParent = true` (padrão na 7.2), o contexto da mensagem vira o
  pai, além de um link. Logo, `Activity.Current` está preenchido durante o
  `ConsumidorEventos.ProcessarAsync`;
- `BasicGetAsync` cria um span `fetch` que termina dentro da chamada (só um link para a origem);
- atributos de convenção semântica: `messaging.system`, `messaging.destination.name`,
  `messaging.destination.routing_key`, `messaging.message.id`, `messaging.operation.type` etc.
  Corpo e cabeçalhos não entram; o tamanho só com `IncludeMessageSizes`.

**Consequências diretas para o OMS, sem código novo**:
- o consumidor que grava eventos no outbox (reserva, pagamento, entrega) já está dentro do span
  da entrega; a costura do R-03 continua a cadeia;
- as novas tentativas (filas de espera) e a ida para a DLQ republicam dentro do span da
  entrega. O `traceparent` copiado dos cabeçalhos originais é sobrescrito pelo atual, então cada
  tentativa vira filha da anterior, no mesmo trace. É o comportamento desejado;
- `ConexaoRabbitMQ` não muda. Basta registrar as fontes: `AddSource("RabbitMQ.Client.*")`.

**Alternativa rejeitada**: criar spans próprios em volta de cada publicação e consumo, como
faria uma biblioteca antiga. Duplicaria o que o cliente já faz.

## R-03 Propagação pelo outbox (ponto central)

**Problema**: o evento é gravado na transação da requisição (ou do consumidor) e publicado
depois, por outro processo, pelo `DespachanteEventosSaida`. Na publicação, `Activity.Current`
é nulo; o RabbitMQ.Client começaria um trace novo.

**Decisão**:
1. **Gravar o contexto**: coluna nova `ContextoRastreio` (`varchar(55)`, nula) em
   `EventosSaida`, com migration, mapeada como **propriedade de sombra** do EF Core (revisão do
   plano). Um interceptor de `SaveChanges` na Infrastructure (`InterceptorContextoRastreio`)
   preenche a coluna das linhas **novas** com `Activity.Current?.Id` (formato W3C
   `00-<trace>-<span>-<flags>`, só quando `IdFormat == W3C`). O Domain e o Application não
   mudam: nenhuma fábrica (`EventoSaida.De`, `Pedido`) passa a conhecer rastreamento, e
   nenhum dos nove pontos que gravam eventos precisa de ajuste. Sem activity, fica nula e tudo
   segue como hoje. O `tracestate` não é gravado: o OMS não usa, e ele pode crescer até 512
   caracteres.
2. **Retomar na publicação**: em `DespachanteEventosSaida.PublicarAsync`, se houver contexto
   válido, abrir uma activity `outbox publicar {tipo}` (fonte `PedidoFulfillment`, `Internal`)
   com esse contexto como pai. O `BasicPublishAsync` cria o span `Producer` dele como filho e
   injeta o `traceparent` dele. O consumidor do outro lado continua o mesmo trace. Sem link
   para o ciclo do despachante: ciclos não geram span (item 3).
3. **Não criar span por ciclo vazio**: o despachante roda a cada 1 s. Spans só por evento
   publicado, nunca pela consulta dos pendentes (ver R-09 sobre o SQL do polling).

O cabeçalho de transporte é o `traceparent` que o próprio cliente injeta, ao lado de
`x-tentativas` e `x-reenvios`. **O envelope v1 não muda** (Princípio II).

**Efeito**: `POST /api/pedidos` → INSERT (outbox) → [tempo no outbox] → `publicar pedido.criado`
→ `publish` → `deliver oms.estoque` → INSERT (outbox, `estoque.reservado`) → `publicar` →
`deliver oms.pagamento` → … → `deliver oms.entrega`. Um único trace; o intervalo entre a
requisição e o `publicar` mostra o tempo de espera no outbox.

**Alternativas rejeitadas**:
- `traceparent` dentro do envelope: muda o contrato v1 (Princípio II);
- reconstruir o contexto a partir do `IdEvento`: o trace id ficaria determinístico, mas sem o
  span pai; a requisição e o consumo virariam traces separados;
- publicar direto na requisição (sem outbox): quebra o Princípio IV.

## R-04 Reenvio da DLQ ligado ao trace original e ao pedido de reenvio

**Problema**: o `ReenvioDlq` lê com `BasicGetAsync` e republica fora de qualquer span da
mensagem. Como o cliente sobrescreve o `traceparent`, a cópia reenviada perderia o trace do
pedido; e sem nada mais, ela nem se ligaria ao pedido de reenvio do Admin.

**Decisão**:
- `ExecutorReenviosDlq` abre um span raiz por pedido de reenvio executado,
  `reenviar_dlq {consumidor}`, com `oms.reenvio.pedido_id`. O registro do pedido na API
  (`POST`) grava, como o outbox, o contexto da requisição numa coluna `ContextoRastreio` em
  `PedidosReenvioDlq`; o span da execução recebe um link para ele.
- para cada mensagem reenviada, uma activity `reenviar {tipo}` cujo **pai é o contexto
  extraído dos cabeçalhos da própria mensagem** (o trace do pedido), com link para o span da
  execução. O `BasicPublishAsync` fica dentro dela e injeta o `traceparent` novo.

Assim a mensagem reenviada continua no trace original (critério de aceite 2) e aponta para o
pedido de reenvio pelo link e pelo cabeçalho `x-pedido-reenvio`, que já existe.

**Custo extra**: uma segunda coluna nula, na mesma migration do R-03. Se o custo pesar, cortar
o link para a requisição do Admin; o `oms.reenvio.pedido_id` já identifica o pedido.

## R-05 Rotinas sem requisição de origem

Reconciliação (REQ-12), estornos (REQ-13), limpeza e aviso da DLQ (REQ-14).

**Decisão**: cada rodada é **um trace novo** (span raiz `Internal`, nome da rotina), e cada
pedido tratado dentro dela é um span filho com `oms.pedido.id` e **um link para o trace de
criação do pedido**. É o padrão que a convenção recomenda para trabalho em lote: o pai
continua sendo a rodada, e o link leva ao pedido.

Para ter o contexto de criação do pedido, coluna nova `ContextoRastreio` em `Pedidos`, gravada
na criação pelo mesmo interceptor (mesma migration). A consulta dos pedidos parados e a dos
estornos pendentes passam a devolver esse contexto junto do pedido. A alternativa de buscar no `EventoSaida` de `pedido.criado` não
serve: o evento não tem coluna de pedido e é apagado pela limpeza depois de 7 dias.

| Rotina | Span raiz | Filhos | Link |
|--------|-----------|--------|------|
| `ReconciliadorPedidos` | `reconciliar` | um por pedido parado | criação do pedido |
| `ProcessadorEstornos` | `processar_estornos` | um por estorno | criação do pedido |
| `ExecutorReenviosDlq` | `reenviar_dlq` | um por mensagem (R-04) | trace da mensagem e pedido do Admin |
| `LimpezaMensageria` | `limpar` | um por alvo, com as linhas apagadas | — |
| `MonitorDlq` | nenhum | — | só métrica (R-07) |

Rodada que não pega o lock (`sp_getapplock`) não gera span: hoje ela vira log de nível Debug
(9006, 7011, 5015), e um trace por instância ociosa a cada minuto seria ruído.

Os eventos que essas rotinas gravam no outbox (por exemplo `pedido.cancelado` pela
reconciliação) herdam o span filho como contexto (R-03): o consumo desse evento fica no trace
da rodada, ligado ao pedido pelo link.

**Alternativa rejeitada**: continuar o trace original do pedido como pai. Um pedido criado há
três dias teria um trace com dias de duração, e uma rodada que trata 100 pedidos ficaria
espalhada em 100 traces.

## R-06 Atributos e o que não pode entrar

**Fontes próprias**: `ActivitySource` e `Meter` com o nome `PedidoFulfillment` (um por
processo é suficiente; o `service.name` separa a API do Worker).

**Atributos próprios** (prefixo `oms.`, como pede a convenção para atributos de domínio):

| Atributo | Onde | Exemplo |
|----------|------|---------|
| `oms.pedido.id` | spans de requisição de pedido, consumo, rotinas | GUID |
| `oms.evento.tipo` | publicar, consumir | `pedido.criado` |
| `oms.consumidor` | consumo, reenvio | `oms.pagamento` |
| `oms.tentativa` | consumo | `2` |
| `oms.reenvio.pedido_id` | reenvio | GUID |
| `oms.resultado` | consumo, rotinas | `processado`, `duplicado`, `nova_tentativa`, `dlq` |
| `user.id` | requisições autenticadas | id do Identity, nunca o e-mail |

`messaging.message.id` (o `IdEvento`) já vem do RabbitMQ.Client.

**Proibido** (mesma regra dos logs, Princípio III):
- parâmetros de SQL: a instrumentação do SqlClient não grava parâmetros; o texto das consultas
  do EF é parametrizado e não carrega valores. Fica gravado só o nome da operação e da tabela.
  O plano decide se mantém o `db.query.text`; a recomendação é mantê-lo, porque mostra a
  consulta lenta sem expor dados;
- corpos de requisição e de mensagem, cabeçalhos `Authorization`, tokens, senhas e e-mails:
  nenhuma instrumentação usada grava, e o plano proíbe enriquecer spans com eles;
- query string: a instrumentação do ASP.NET Core já mascara `url.query` por padrão;
- mensagens de exceção: `RecordException` grava a mensagem; as do OMS não carregam dados
  sensíveis (já são logadas hoje), mas o plano inclui um teste que procura e-mail e token nos
  spans exportados (critério de aceite 4).

## R-07 Métricas

Nomes no padrão da convenção (namespace, ponto, unidade UCUM entre chaves), com prefixo `oms.`
porque não existe convenção para esses conceitos de negócio:

| # | Métrica | Instrumento | Unidade | Atributos | Fonte do valor | Vale o custo? |
|---|---------|-------------|---------|-----------|----------------|---------------|
| 1 | `oms.outbox.eventos_pendentes` | gauge observável | `{evento}` | — | amostrador (abaixo) | **Sim**, principal sinal de despachante parado |
| 2 | `oms.outbox.pendente_mais_antigo.idade` | gauge observável | `s` | — | amostrador | **Sim**, alerta natural ("> 60 s") |
| 3 | `oms.mensageria.dlq.mensagens` | gauge observável | `{mensagem}` | `oms.consumidor` | última leitura do `MonitorDlq` | **Sim**, complementa o 5011 |
| 4 | `oms.mensageria.mensagens` | contador | `{mensagem}` | `oms.consumidor`, `oms.resultado` | `ConsumidorEventos` | **Sim**, substitui contar o 5005/5006 |
| 5 | `oms.rotinas.itens` | contador | `{item}` | `oms.rotina`, `oms.resultado` | reconciliação, estornos, reenvio | **Sim**, barato |
| 6 | `oms.limpeza.linhas_apagadas` | contador | `{linha}` | `oms.limpeza.alvo` | `LimpezaMensageria` | **Sim**, substitui o 5013 |
| — | `oms.estornos.pendentes` | — | — | — | — | **Não** neste REQ: a spec fixou seis métricas |
| — | latência por etapa | — | — | — | — | **Adiar**: o trace mostra por pedido; a métrica exige decidir a origem do tempo (criação ou etapa anterior) e cuidar de reentregas |
| — | `messaging.process.duration` | — | — | — | — | **Não** neste REQ: o trace já mede cada consumo; entra com a latência por etapa |

Valores de `oms.resultado` (revisão do plano):
- métrica 4: `processado`, `duplicado`, `nova_tentativa`, `dlq`, `irrecuperavel`;
- métrica 5, por item tratado: reconciliação (`oms.rotina = reconciliacao`) `recuperado`,
  `esgotado`, `falhou`; estornos (`estornos`) `confirmado`, `falhou`; reenvio da DLQ
  (`reenvio_dlq`, por pedido de reenvio) `concluido`, `falhou`.

**Amostrador** (`AmostradorOutbox`, só no Worker e só com a telemetria ligada): os callbacks de
gauge observável são síncronos e rodam a cada exportação; não podem ir ao banco. O serviço
consulta a cada 30 s `COUNT_BIG(*)` e `MIN(CriadoEm)` dos pendentes (o índice filtrado
`IX_EventosSaida_Pendentes` já existe) e guarda os valores; os gauges só leem. Sem pendentes,
a idade é 0. Banco fora: mantém o último valor e loga em Debug (sem ID novo de aviso).

**DLQ** (clarify, pergunta 2): com a telemetria ligada, o `MonitorDlq` lê a cada 1 min
(`ReenvioDlq:MetricaIntervaloSegundos`, padrão 60) e guarda a última contagem por consumidor
para o gauge 3; o log 5011 sai no máximo a cada `AvisoIntervaloSegundos` (300), contado desde o
último aviso. Com a telemetria desligada, nada muda: leitura e aviso a cada 5 min. Com o aviso
desligado (`AvisoHabilitado = false`) e a telemetria ligada, lê só para a métrica.

**Várias instâncias** (clarify, pergunta 3): cada instância publica com `service.instance.id`
(padrão do SDK: um GUID por processo); os gauges 1 a 3 saem iguais de todas, e o painel usa o
máximo; os contadores 4 a 6 se somam.

**Envio das métricas** (analyze I1): o OMS define 30 s como intervalo padrão de envio (o do
SDK é 60 s), sobrescrito por `OTEL_METRIC_EXPORT_INTERVAL`. Com a leitura de 30 s, o outbox
parado aparece no destino em até 60 s (SC-004); a DLQ, em até 90 s.

## R-08 Logs correlacionados

**Decisão**:
- `builder.Logging.AddOpenTelemetry(...)` exporta os logs por OTLP com `TraceId` e `SpanId` do
  `Activity.Current`, os atributos do `LoggerMessage` (`IdEvento`, `Consumidor`…) e o
  `EventId`;
- no console, `ActivityTrackingOptions = TraceId | SpanId` no `LoggerFactoryOptions` e
  `IncludeScopes` no formatador, para quem olha o `docker compose logs`;
- nenhum `EventId` muda, nenhuma mensagem muda: os IDs documentados em `docs/fluxos/` valem.

Os logs das rotinas (9001, 7006, 5009…) ficam dentro dos spans do R-05; os do consumidor,
dentro do `deliver`. Isso resolve o caso que motivou a issue: a partir do pedido, o trace leva
a todos os logs da API e do Worker na ordem.

## R-09 Ruído: o SQL do polling

Sem cuidado, cada consulta periódica sem span pai vira um trace próprio:

- despachante: 1 consulta por segundo por Worker (86 400 traces por dia, sem valor);
- `ExecutorReenviosDlq`, amostrador do R-07 e lock das rotinas.

**Decisão**: um amostrador próprio, em volta do `ParentBased`, descarta **spans raiz** da fonte
do SqlClient. Consultas dentro de uma requisição, de um consumo ou de uma rodada (que têm pai)
continuam. O plano cobre isso com um teste.

**Alternativa rejeitada**: o `Filter` da instrumentação do SqlClient recebe o comando, não sabe
se há pai, e exigiria listar as consultas por texto.

## R-10 Destino dos dados

**Local**: Aspire Dashboard como contêiner independente no `docker-compose.yml`, sem trocar o
compose pelo Aspire AppHost (fora do escopo; a demo continuaria só com Docker):

```yaml
  aspire-dashboard:
    profiles: [demo, observabilidade]
    image: mcr.microsoft.com/dotnet/aspire-dashboard:13.6.0   # 2026-10-01; a 13.6.1 tem menos de 2 semanas
    environment:
      ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS: "true"     # só local; dados em memória
    ports:
      - "${OMS_DASHBOARD_PORTA:-18888}:18888"                  # interface
      - "${OMS_OTLP_PORTA:-4317}:18889"                        # OTLP gRPC, para a API pelo SDK
```

- `api` e `worker` do perfil demo recebem `OTEL_EXPORTER_OTLP_ENDPOINT=http://aspire-dashboard:18889`;
- com a API pelo SDK (fluxo do REQ-02), `docker compose --profile observabilidade up -d` sobe só
  o painel, e o `appsettings.Development.json` aponta para `http://localhost:4317`;
- o painel mostra traces (com links), métricas e logs estruturados, e guarda tudo em memória:
  reiniciar o contêiner apaga. Basta para a demo e o desenvolvimento.

**Outros destinos, só por configuração** (variáveis padrão do OTel, sem código):

| Destino | Como |
|---------|------|
| Jaeger | aceita OTLP direto: `OTEL_EXPORTER_OTLP_ENDPOINT=http://jaeger:4317` (só traces) |
| Grafana (Tempo, Prometheus/Mimir, Loki) | OpenTelemetry Collector ou Grafana Alloy recebendo OTLP e distribuindo |
| Azure Monitor | Collector com o exportador `azuremonitor`; sem pacote de fornecedor no código |
| Qualquer outro | Collector como intermediário |

`OTEL_SERVICE_NAME` (padrão `oms-api` e `oms-worker`), `OTEL_RESOURCE_ATTRIBUTES`
(`deployment.environment.name`) e `OTEL_EXPORTER_OTLP_PROTOCOL` também por variável.

## R-11 Desligado, destino fora do ar, amostragem e desempenho

**Desligado**: sem `OTEL_EXPORTER_OTLP_ENDPOINT` e sem `Observabilidade:Habilitada = true`
(usado só pelos testes, que coletam sem exportar), o OpenTelemetry nem é registrado. Sem ouvinte, o ASP.NET Core e o RabbitMQ.Client não criam
activities, `Activity.Current` fica nulo, as colunas `ContextoRastreio` ficam nulas, e o
comportamento é o de hoje. É o padrão dos testes existentes, que não mudam.

**Destino fora do ar**: o exportador OTLP trabalha em lote, em segundo plano, com fila
limitada (2 048 spans) e prazo de 10 s por envio. Fora do ar, descarta e segue; a API e o
Worker não esperam nem falham (critério de aceite 6, coberto por teste com endpoint inválido).

**Amostragem**:
- local e demo: `ParentBased(AlwaysOn)`, tudo;
- produção: `ParentBased(TraceIdRatioBased(r))`, com `r` por configuração
  (`Observabilidade:TaxaAmostragem`, padrão 1,0);
- como o contexto viaja pelo outbox com a flag de amostragem, a decisão da requisição vale
  para o pedido inteiro: ou o trace vem completo, ou não vem;
- as métricas e os logs não são amostrados.

**Desempenho** (ordem de grandeza, a medir no plano):
- spans por pedido no caminho feliz: cerca de 25 (1 requisição, ~6 SQL na API, e por etapa:
  publicar, publish, deliver e ~4 SQL), ou seja, ~1 a 3 µs de CPU cada e poucos KB por pedido
  exportados em lote;
- o OMS está longe de um volume em que isso pese; a amostragem resolve se pesar;
- a coluna nova soma 55 bytes por evento e por pedido.

## R-12 Testes

- **Unitários**: `EventoSaida.De` grava o `Activity.Current.Id` e nulo sem activity; o
  amostrador do R-09; os nomes e unidades das métricas.
- **Integração (banco e broker reais, Testcontainers, como hoje)**, com o SDK ligado por
  `Observabilidade:Habilitada = true` e um coletor de teste registrado como `BaseProcessor<Activity>`
  e `BaseProcessor<LogRecord>` (classes do próprio SDK, que já vem pelo
  `OpenTelemetry.Extensions.Hosting`), mais um `MeterListener` da BCL para as métricas. Assim o
  amostrador do R-09 também é exercitado, e não entra o pacote `OpenTelemetry.Exporter.InMemory`.
  Como a API e o Worker rodam no mesmo processo de teste, cada fábrica registra o próprio coletor
  e os testes filtram pelo trace id:
  - criar um pedido gera um só trace da rota `POST` até os três consumidores (aceite 1);
  - uma mensagem da DLQ reenviada continua o trace original e tem link para o pedido de
    reenvio (aceite 2);
  - um ciclo de nova tentativa fica no mesmo trace;
  - os gauges do outbox e da DLQ refletem o estado (aceite 3);
  - nenhum atributo exportado contém e-mail, token ou senha (aceite 4);
  - com OTel desligado e com endpoint inválido, os fluxos passam (aceite 6).
- A suíte atual (1.033 testes) roda sem OTel e precisa continuar verde sem mudança.

## Custo estimado

| Parte | O que entra | Tamanho |
|-------|-------------|---------|
| Base (SDK, OTLP, ASP.NET Core, SqlClient, RabbitMQ, logs, amostrador do R-09) | `AdicionarObservabilidade` na Infrastructure, chamada pela API e pelo Worker; configuração | Pequeno: ~1 fase |
| Propagação pelo outbox e pelo reenvio | 3 colunas nulas (1 migration), `EventoSaida.De`, despachante, `ReenvioDlq`, `ExecutorReenviosDlq` | Médio: ~1 fase |
| Spans das rotinas | 4 rotinas, links | Pequeno |
| Métricas | 6 instrumentos, amostrador, `MonitorDlq` | Médio: ~1 fase |
| Compose e documentação | painel no compose, `.env.example`, `docs/fluxos/` (08, 09, 10, 11 e o índice), quickstart | Pequeno |
| Testes de integração do trace | listener, cenários do R-12 | Médio |

Somado, fica no tamanho do REQ-14: quatro ou cinco fases de implementação.

## Riscos

1. Compatibilidade da instrumentação do SqlClient 1.19 com o Microsoft.Data.SqlClient 7.1.1
   (R-01): validar no começo da implementação.
2. Mudança do padrão `UsePublisherAsParent` numa versão futura do RabbitMQ.Client: o
   consumidor passaria a ter só link, e o trace se partiria em vários. Um teste de integração
   (aceite 1) pega isso na atualização.
3. Volume do painel local: o Aspire Dashboard guarda em memória com limite; numa demo longa,
   os traces antigos somem. É esperado.

## Fora do escopo (mantido da issue)

- trocar o compose pelo Aspire AppHost;
- alertas em produção e fornecedor pago;
- mudar o envelope v1 ou os contratos públicos da API;
- métrica de latência por etapa (adiada, ver R-07).

## Documentos de fluxo afetados (se adotado)

`08-publicacao-e-consumo-de-eventos.md` (cabeçalho `traceparent`, coluna no outbox, métricas
4), `09-reconciliacao.md` e `10-estorno.md` (spans e métrica 5), `11-limpeza-e-reenvio-da-dlq.md`
(reenvio no trace, métricas 3 e 6; a limitação "Não há métricas nem alertas além dos logs" vira
"não há alertas", com a issue #37 no lugar da pendência de métricas) e o índice.

## Fontes

- RabbitMQ.Client 7.2, `RabbitMQActivitySource.cs`, `RabbitMQTracingOptions.cs`,
  `AsyncConsumerDispatcher.cs` e `Channel.BasicPublish.cs`:
  <https://github.com/rabbitmq/rabbitmq-dotnet-client/tree/v7.2.0/projects/RabbitMQ.Client>
- Versões e datas: <https://api.nuget.org/v3-flatcontainer/> (consultado em 2026-10-26)
- Aspire Dashboard independente e configuração:
  <https://learn.microsoft.com/en-us/dotnet/aspire/dashboard>,
  <https://aspire.dev/dashboard/configuration/>,
  <https://aspire.dev/dashboard/security-considerations/>
- Instrumentação do SqlClient: <https://www.nuget.org/packages/OpenTelemetry.Instrumentation.SqlClient>
- Instrumentação do EF Core (beta): <https://www.nuget.org/packages/OpenTelemetry.Instrumentation.EntityFrameworkCore>
